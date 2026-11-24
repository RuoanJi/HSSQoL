"""Builds jobs.html from jobs.md (parses its four tables) + jobs_template.html.

Re-run after jobs.md changes:  python make_jobs_html.py
"""
import json, os, re

HERE = os.path.dirname(os.path.abspath(__file__))
md = open(os.path.join(HERE, 'jobs.md'), encoding='utf-8').read()
KN = ['近战', '远程', '海员', '探究心', '工作心', '勇敢战士', '秘传']


def tables(text):
    out, cur = [], None
    for line in text.splitlines():
        if line.startswith('|'):
            cells = [c.strip() for c in line.strip()[1:-1].split('|')]
            if cur is None: cur = []; out.append(cur)
            if not all(re.fullmatch(r':?-+:?', c) for c in cells): cur.append(cells)
        else:
            cur = None
    return out


t_main, t_other, t_know, t_bed = tables(md)
num = lambda s: int(s.replace(',', '').replace('+', ''))
split = lambda s: [x.strip() for x in re.split(r'<br>|,', s) if x.strip()]

jobs = []
for r in t_main[1:]:
    req = {}
    for part in split(r[4]):
        k, lv = part.rsplit(' Lv', 1); req[k] = int(lv)
    jobs.append({'id': int(r[0]), 'name': r[1], 'maxLv': int(r[2]), 'exp': num(r[3]), 'req': req,
                 'stats': [[int(a) for a in c.split('→')] for c in r[5:11]],
                 'mastery': [num(c) for c in r[11:17]], 'skill': r[17]})
J = {j['id']: j for j in jobs}
for r in t_other[1:]:
    j = J[int(r[0])]
    j.update({'good': split(r[2]), 'bad': split(r[3]), 'fav': split(r[4]), 'act': int(r[5]), 'work': int(r[6]),
              'getExp': int(r[7]), 'cls': int(r[8]), 'rank': int(r[9]), 'masterNum': int(r[10]), 'medal': int(r[11]),
              'desc': r[12]})
for r in t_bed[1:]:
    j = J[int(r[0].split()[0])]
    j['bed'] = {'room': r[1], 'param': r[2], 'area': r[3], 'extra': '' if r[4] == '—' else r[4]}
know = [{'k': r[0], 'all': r[1], 'noMedal': r[2]} for r in t_know[1:]]

head = md.split('\n|')[0]
m = re.search(r'Exported from[^\n]*', head)
pay = json.dumps({'jobs': jobs, 'know': know, 'kn': KN, 'src': m.group(0) if m else ''},
                 ensure_ascii=False, separators=(',', ':')).replace('</', '<' + chr(92) + '/')
tpl = open(os.path.join(HERE, 'jobs_template.html'), encoding='utf-8').read()
open(os.path.join(HERE, 'jobs.html'), 'w', encoding='utf-8').write(tpl.replace('/*JOB_DATA*/null', pay))
print('jobs ok', len(jobs), sum(1 for j in jobs if j['medal']), 'medal jobs')
