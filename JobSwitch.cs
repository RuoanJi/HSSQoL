using System;
using BepInEx.Configuration;
using data;
using form;
using game;
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
    internal static ConfigEntry<bool> Enabled, AllowMedalJobs;
    private const int SkillTypeShield = 28;   // a skill of this type lets the character keep a shield
    private const int EquipShield = 1;        // Character.GetEquip / Equip slot 1

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("JobSwitch", "Enabled", true,
            "Show the 转职满级 button on the job-change page (next to 强化满级).");
        AllowMedalJobs = cfg.Bind("JobSwitch", "AllowMedalJobs", false,
            "Also pick jobs that cost medals to switch to (the medals are spent, like the game's own switch). Off: those jobs are skipped.");
    }

    private static void Log(string s) => Plugin.L.LogInfo($"[jobswitch] {s}");

    internal static void ButtonPressed(SubForm page, Character c, Human h)
    {
        AutoFeed.Safe(() =>
        {
            if (c == null) { AutoFeed.Show("JobSwitch: couldn't tell which character this page is for."); return; }
            var cur = c.GetJob();
            string curName = cur?.name_ ?? $"#{c.jobID_}";

            // 1. Current job must be at max level.
            if (!c.IsJobLvMax())
            {
                AutoFeed.Show($"JobSwitch: {c.GetName()} {curName} is Lv {c.GetLv()}/{c.GetLvMax()}, not max; nothing done.");
                return;
            }
            // At max but the job-clear bonus not granted yet: grant it first through the game's window,
            // otherwise switching away would skip it.
            if (!c.IsMastedJob())
            {
                AutoFeed.OpenJobClearWindow(c, page);
                AutoFeed.Show($"JobSwitch: {curName} is at max but not mastered yet; opened the Job Master window. Press again after closing it.");
                return;
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
                return;
            }

            int oldId = c.jobID_;
            Switch(c, target, app);
            Log($"{c.GetName()}: {curName} (id {oldId}) -> {target.name_} (id {target.id_})");

            // The page lists jobs and keeps a cursor on the current one; rebuild it like opening the page does.
            try { page.InitJobChange(); } catch (Exception e) { Log($"InitJobChange failed: {e.Message}"); }

            // 3. 强化满级 on the new job.
            AutoFeed.ButtonPressed(h, c, page);
        });
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

        if (c.GetSetSkill(SkillTypeShield) == null) c.Equip(EquipShield, null);
    }
}
