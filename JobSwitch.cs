using System;
using BepInEx.Configuration;
using data;
using form;
using game;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace HSSQoL;

/// <summary>
/// "转职满级" on the job-change page: when the current job is at max level, switch to the first job (lowest
/// index) that isn't mastered yet and that the character can switch to, then run 强化满级 on it.
///
/// The switch itself repeats what the page does when you confirm a job change (SubForm.ProJobChange, the
/// "yes" branch of its confirm dialog), step by step:
///   1. jobs with a medal cost spend it: player.AddResearchPoint(0, 1, -medalNum_)
///   2. for each knowledge requirement [type, lv]: knowExp_[type] -= GetKnowLvExp(type), knowLv_[type] -= lv
///      (each clamped at 0), i.e. switching spends the required knowledge levels
///   3. lv_ = 1, exp_ = 0, jobID_ = new job
///   4. battle HP/SP clamped to the new maximums
///   5. skills: if CheckLearnSKill() is false, re-add the assist skill when it fits (CheckSkill == 0);
///      otherwise unequip the base skill and remember it as the assist skill
///   6. no skill that allows a shield (GetSetSkill(28) == null) → unequip the shield, Equip(1, null)
/// Eligibility is the game's own Character.CheckJobChange(job) == -1 (knowledge levels, number of mastered
/// jobs, medals). The game's shield check before the switch (no shield skill and the shield can't go back to
/// the inventory → it refuses) is applied too: such jobs are skipped.
/// Not repeated: the confirm dialog and the "job changed" window (CreateJobChange(crew, oldJob)); the feed's
/// Job Master window follows instead.
/// </summary>
internal static class JobSwitch
{
    internal static ConfigEntry<bool> Enabled, ChainEnabled, AllowMedalJobs, RestoreSkill;
    private const int SkillTypeShield = 28;   // a skill of this type lets the character keep a shield
    private const int EquipShield = 1;        // Character.GetEquip / Equip slot 1

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("JobSwitch", "Enabled", true,
            "Show the 转职满级 button on the job-change page (next to 强化满级).");
        ChainEnabled = cfg.Bind("JobSwitch", "ChainButton", true,
            "Show the 连续转职 button on the job-change page (right of 道具): repeats 转职满级 until no job can be switched to or every job has been mastered this round. Press it again to stop.");
        AllowMedalJobs = cfg.Bind("JobSwitch", "AllowMedalJobs", false,
            "Also pick jobs that cost medals to switch to (the medals are spent, like the game's own switch). Off: those jobs are skipped.");
        RestoreSkill = cfg.Bind("JobSwitch", "RestoreSkill", true,
            "After any job change (the game's own or 转职满级), re-equip the skill the game took off when the character switched into that skill's job. The game only does this when the new job's skill isn't learned yet, so it never happens when re-mastering jobs. Uses the game's own saved field; the mod stores nothing.");
    }

    private static void Log(string s) => Plugin.L.LogInfo($"[jobswitch] {s}");

    /// <summary>What one 转职满级 press did (used by 连续转职 to decide whether to go on).</summary>
    internal enum StepResult { Error, NoCharacter, NotMax, OpenedClearWindow, NoTarget, Switched }

    internal static void ButtonPressed(SubForm page, Character c, Human h)
    {
        if (JobChain.Active) { AutoFeed.Show("JobSwitch: 连续转职 is running; press 连续转职 to stop it first."); return; }
        Step(page, c, h);
    }

    internal static StepResult Step(SubForm page, Character c, Human h)
    {
        var result = StepResult.Error;
        AutoFeed.Safe(() =>
        {
            result = StepCore(page, c, h);
        });
        return result;
    }

    private static StepResult StepCore(SubForm page, Character c, Human h)
    {
        {
            if (c == null) { AutoFeed.Show("JobSwitch: couldn't tell which character this page is for."); return StepResult.NoCharacter; }
            var cur = c.GetJob();
            string curName = cur?.name_ ?? $"#{c.jobID_}";

            // 1. Current job must be at max level.
            if (!c.IsJobLvMax())
            {
                AutoFeed.Show($"JobSwitch: {c.GetName()} {curName} is Lv {c.GetLv()}/{c.GetLvMax()}, not max; nothing done.");
                return StepResult.NotMax;
            }
            // At max but the job-clear bonus not granted yet: grant it first through the game's window,
            // otherwise switching away would skip it.
            if (!c.IsMastedJob())
            {
                AutoFeed.OpenJobClearWindow(c, page);
                AutoFeed.Show($"JobSwitch: {curName} is at max but not mastered yet; opened the Job Master window. Press again after closing it.");
                return StepResult.OpenedClearWindow;
            }

            // 2. First job, by index, that isn't mastered and that the game would let us switch to.
            var app = main.AppData.GetInstance();
            var jobs = app.jobList_;
            Job target = null;
            for (int i = 0; i < jobs.Length; i++)
            {
                var job = jobs[i];
                if (job == null || job.id_ == c.jobID_) continue;
                if (c.CheckJobMaster(job)) continue;                   // already mastered
                int check = c.CheckJobChange(job);
                if (check != -1) continue;                             // knowledge / mastered-count / medals
                if (job.medalNum_ > 0 && !AllowMedalJobs.Value)
                {
                    Log($"skipping {job.name_} (id {job.id_}): costs {job.medalNum_} medal(s), AllowMedalJobs is off");
                    continue;
                }
                if (ShieldBlocks(c, job, app))
                {
                    Log($"skipping {job.name_} (id {job.id_}): the shield would have to be removed but there's no room for it");
                    continue;
                }
                target = job;
                break;
            }
            if (target == null)
            {
                AutoFeed.Show($"JobSwitch: no unmastered job that {c.GetName()} can switch to right now.");
                return StepResult.NoTarget;
            }

            int oldId = c.jobID_;
            Switch(c, target, app);
            Log($"{c.GetName()}: {curName} (id {oldId}) -> {target.name_} (id {target.id_})");

            // The page lists jobs and keeps a cursor on the current one; rebuild it like opening the page does.
            try { page.InitJobChange(); } catch (Exception e) { Log($"InitJobChange failed: {e.Message}"); }

            // 3. 强化满级 on the new job.
            AutoFeed.ButtonPressed(h, c, page);
            return StepResult.Switched;
        }
    }

    /// <summary>ProJobChange's pre-switch shield check: true when the game would refuse the switch.</summary>
    private static bool ShieldBlocks(Character c, Job job, main.AppData app)
    {
        var shield = c.GetEquip(EquipShield);
        if (shield == null) return false;
        var skills = app.skillList_;
        var set = c.skill_;
        if (set != null)
            for (int i = 0; i < set.Size(); i++)
            {
                var e = set.ElementAt(i)?.TryCast<Il2CppStructArray<int>>();
                if (e == null || e.Length == 0) continue;
                int id = e[0];
                if (id >= 0 && id < skills.Length && skills[id]?.type_ == SkillTypeShield) return false;
            }
        var js = job.GetSkill();
        if (js != null && js.type_ == SkillTypeShield) return false;
        return !shield.AddCheck(1);
    }

    /// <summary>The "yes" branch of SubForm.ProJobChange's confirm dialog.</summary>
    private static void Switch(Character c, Job job, main.AppData app)
    {
        if (job.medalNum_ > 0) app.player_.AddResearchPoint(0, 1, -job.medalNum_);

        var req = job.jobChange_;
        if (req != null)
            for (int r = 0; r < req.Length; r++)
            {
                var p = req[r];
                if (p == null || p.Length < 2) continue;
                int type = p[0], lv = p[1];
                var kExp = c.knowExp_;
                kExp[type] = Math.Max(0, kExp[type] - c.GetKnowLvExp(type));
                var kLv = c.knowLv_;
                kLv[type] = Math.Max(0, kLv[type] - lv);
            }

        c.lv_ = 1;
        c.exp_ = 0;
        c.jobID_ = job.id_;

        int hpMax = c.GetHPMax();
        if (hpMax < c.battleHP_) c.battleHP_ = hpMax;
        int spMax = c.GetMPMax();
        if (spMax < c.battleSP_) c.battleSP_ = spMax;

        var skills = app.skillList_;
        if (!c.CheckLearnSKill())
        {
            int a = c.assistSkillID_;
            if (a >= 0 && a < skills.Length)
            {
                var s = skills[a];
                if (s != null && c.CheckSkill(s) == 0)
                {
                    var entry = new Il2CppStructArray<int>(1);
                    entry[0] = s.id_;
                    c.skill_.AddElement(new Il2CppSystem.Object(entry.Pointer));
                }
            }
        }
        else
        {
            var baseSkill = c.GetBaseSkill();
            var set = c.skill_;
            if (baseSkill != null && set != null)
                for (int i = set.Size() - 1; i >= 0; i--)
                {
                    var e = set.ElementAt(i)?.TryCast<Il2CppStructArray<int>>();
                    if (e == null || e.Length == 0 || e[0] != baseSkill.id_) continue;
                    c.RemoveSkill(i);
                    c.assistSkillID_ = e[0];
                    break;
                }
        }

        RestoreAssistSkill(c, "转职满级");

        if (c.GetSetSkill(SkillTypeShield) == null) c.Equip(EquipShield, null);
    }

    /// <summary>
    /// Re-equips the skill remembered in <c>assistSkillID_</c>. On a job change the game unequips the new
    /// job's own skill (the job gives it anyway) and remembers it there, but it only puts it back when the
    /// next job's skill isn't learned yet (<c>!CheckLearnSKill()</c>). When re-mastering jobs every job's
    /// skill is learned, so the remembered skill never came back. This does the same re-add on every job
    /// change; <c>CheckSkill == 0</c> already rules out the current job's own skill, an already equipped
    /// skill and a full set of slots. Nothing new is saved: <c>assistSkillID_</c> is the game's own field.
    /// </summary>
    internal static void RestoreAssistSkill(Character c, string where)
    {
        if (!RestoreSkill.Value || c == null) return;
        var skills = main.AppData.GetInstance()?.skillList_;
        int a = c.assistSkillID_;
        if (skills == null || a < 0 || a >= skills.Length) return;
        var s = skills[a];
        if (s == null || c.skill_ == null) return;
        int check = c.CheckSkill(s);
        if (check == 0)
        {
            var entry = new Il2CppStructArray<int>(1);
            entry[0] = s.id_;
            c.skill_.AddElement(new Il2CppSystem.Object(entry.Pointer));
            Log($"{where}: {c.GetName()} re-equipped {s.name_} (id {s.id_})");
        }
        else if (check == 2)
            Log($"{where}: {c.GetName()} couldn't re-equip {s.name_} (id {s.id_}): no free skill slot");
    }

    /// <summary>
    /// The game's own job change: SubForm.ProJobChange calls CreateJobChange(crew, oldJobID) for its result
    /// window right after the skill and shield handling. Nothing else calls this overload.
    /// </summary>
    [HarmonyPatch(typeof(SubForm), nameof(SubForm.CreateJobChange), typeof(Crew), typeof(int))]
    internal static class GameSwitchPatch
    {
        private static void Prefix(Crew crew)
        {
            try { RestoreAssistSkill(crew?.TryCast<Character>(), "转职"); }
            catch (Exception e) { Plugin.L.LogError($"[jobswitch] RestoreAssistSkill: {e}"); }
        }
    }
}
