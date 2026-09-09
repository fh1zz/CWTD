using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.Serialization.Json;
using PetTD;

public static class BalanceProbe
{
    static GameConfig Load(string path) { using(var f=File.OpenRead(path)) return (GameConfig)new DataContractJsonSerializer(typeof(GameConfig)).ReadObject(f); }
    public static int Main(string[] args)
    {
        Console.WriteLine("config,level,scenario,seed,team,result,wave,lives,kills,draws,spent,coins,maxTowers,maxTier,time");
        for(int l=0;l<3;l++)
        {
            GameConfig c=Load(args[0]);
            if(args.Length<2 || args[1]!="managed")
                for(int a=0;a<c.pets.Length;a++) for(int b=a;b<c.pets.Length;b++)
                {
                    Run(c,l,new[]{c.pets[a].id,c.pets[b].id},b==a?1:2,false,true,7,args[0]);
                    if(a==b) Run(c,l,new[]{c.pets[a].id,c.pets[b].id},2,false,true,7,args[0]);
                }
            foreach(int seed in new[]{7,19,41,73,101})
            {
                c=Load(args[0]); Run(c,l,c.pets.Take(c.levels[l].slots).Select(p=>p.id).ToArray(),3,true,true,seed,args[0]);
                c=Load(args[0]); Run(c,l,c.pets.Take(c.levels[l].slots).Select(p=>p.id).ToArray(),3,true,false,seed,args[0]);
                c=Load(args[0]); Run(c,l,new[]{"fox","otter","falcon","sprout","deer"}.Take(c.levels[l].slots).ToArray(),3,true,true,seed,args[0]);
                c=Load(args[0]); Run(c,l,new[]{"fox","otter","falcon","sprout","deer"}.Take(c.levels[l].slots).ToArray(),3,true,false,seed,args[0]);
            }
        }
        return 0;
    }
    static int[] BestPads(GameModel m, string id)
    {
        float range=m.Spec(id).range;
        return Enumerable.Range(0,m.Pads.Count).OrderByDescending(i=> {
            float length=0;
            for(int n=1;n<m.Path.Count;n++) if(m.Distance(m.Pads[i],m.Path[n])<range)
                length+=m.Distance(m.Path[n-1],m.Path[n]);
            return length;
        }).ThenBy(i=>i).ToArray();
    }
    static void Run(GameConfig c,int level,string[] chosen,int count,bool manage,bool star,int seed,string label)
    {
        var m=new GameModel(c,level,chosen.Distinct().ToArray(),seed);
        if(!manage)
        {
            m.Pool.Clear();
            for(int i=0;i<count;i++)
            {
                string id=chosen[Math.Min(i,chosen.Length-1)];
                var p=new Pet{Uid=100+i,Species=id,Level=1,Pad=-1,ActiveRemaining=3};m.Pool.Add(p);
                foreach(int pad in BestPads(m,id)) if(!m.Towers.Any(t=>t.Pad==pad)) {m.Deploy(p.Uid,pad);break;}
            }
        }
        int draws=0,maxT=0,maxL=0,guard=0;
        while((m.Stage==RunStage.Preparing||m.Stage==RunStage.Running)&&guard++<6000)
        {
            if(manage)
            {
                if(m.CanEvolvePool())m.EvolvePool();
                foreach(var p in m.Towers.ToArray())
                    if(m.CanEvolve(p.Uid)&&(m.Towers.Count>=12||m.Pool.Count(t=>t.Species==p.Species&&t.Level==p.Level)>=2)) m.EvolveTower(p.Uid);
                foreach(var p in m.Pool.ToArray()) foreach(int pad in BestPads(m,p.Species))
                    if(!m.Towers.Any(t=>t.Pad==pad)){m.Deploy(p.Uid,pad);break;}
                while(m.Coins>=c.drawCost&&m.Pool.Count<c.poolCapacity) {if(!m.DrawPet())break;draws++;}
            }
            maxT=Math.Max(maxT,m.Towers.Count); foreach(var p in m.Towers)maxL=Math.Max(maxL,p.Level);
            if(m.Stage==RunStage.Preparing)m.StartWave();
            if(star&&m.SkillRemaining<=0&&m.Enemies.Count>0)
            {
                var target=m.Enemies.OrderByDescending(e=>e.Progress).First(); m.CastSkill(target.X,target.Y);
            }
            m.Tick(.25f);
        }
        Console.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14:F1}",
            Path.GetFileName(label),m.Level.id,manage?(star?"managed-star":"managed-no-star"):("idle-"+count+"-star"),seed,
            string.Join("+",chosen),m.Stage,m.Wave,m.Lives,m.Kills,draws,draws*c.drawCost,m.Coins,maxT,maxL,m.Elapsed);
    }
}
