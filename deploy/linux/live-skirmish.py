# Prints how many Skirmish (simulation) rooms are running now, from the server log since the last start.
# deploy/deploy.sh pipes it to the VPS (python3 -) to count Skirmish matches as missions.
import re,datetime
L=open('/var/log/huxley/server.log','rb').read().decode('latin1').splitlines()
start=max((i for i,l in enumerate(L) if 'Settings: ' in l),default=0)
live={}
for l in L[start:]:
    m=re.match(r'\[(\S+ \S+)\] \[Simulation\] (started room (\d+)|room (\d+) (match finished|aborted by host))',l)
    if not m: continue
    t=datetime.datetime.strptime(m.group(1),'%Y-%m-%d %H:%M:%S')
    if m.group(3): live[m.group(3)]=t
    else: live.pop(m.group(4),None)
now=datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None)
print(sum(1 for t in live.values() if (now-t).total_seconds()<45*60))
