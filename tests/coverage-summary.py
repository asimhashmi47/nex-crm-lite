"""Summarises a Cobertura report: per assembly, and for the business-rule and auth classes the QA plan targets at 100%."""
import sys, xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
for p in root.iter('package'):
    print(f"{p.get('name'):28} line {float(p.get('line-rate'))*100:5.1f}%  branch {float(p.get('branch-rate'))*100:5.1f}%")

RULES = ('NexCrm.Domain.Entities', 'NexCrm.Domain.Common', 'NexCrm.Application.Common.CrudService', 'NexCrm.Application.Features')
DATA = ('Dto', 'Request', 'Validator', 'Options', 'Result', 'Option', 'Column', 'Card')
lines = covered = branches = branches_hit = 0
missed_report = []
for c in root.iter('class'):
    name = c.get('name')
    if not name.startswith(RULES) or any(k in name for k in DATA):
        continue
    for line in c.iter('line'):
        lines += 1
        covered += line.get('hits') != '0'
        if line.get('branch', '').lower() == 'true':
            hit, total = line.get('condition-coverage').split('(')[1].rstrip(')').split('/')
            branches += int(total); branches_hit += int(hit)
    missed = sorted({int(l.get('number')) for l in c.iter('line') if l.get('hits') == '0'})
    if missed:
        missed_report.append(f"  {name[:72]:72} lines {missed}")

print(f"\nBusiness rules + auth: line {covered/lines*100:.1f}% ({covered}/{lines})  branch {branches_hit/max(branches,1)*100:.1f}% ({branches_hit}/{branches})")
print("\n".join(missed_report) or "  nothing uncovered")
