using System;
using System.Collections.Generic;

namespace PetTD
{
    [Serializable]
    public class AutoSkillSpec
    {
        public string name, effect, description;
        public float cooldown, initialDelay, damageRatio, rangeMultiplier, radius, duration;
        public float poisonDps, burnRatio, slowPower, rootDuration, vulnerability;
        public float allyDamageBonus, allyAttackSpeedBonus, executeThreshold, eliteMultiplier, eliteControlMultiplier;
        public int targets;
    }

    public partial class GameModel
    {
        public AutoSkillSpec AutoSkill(Pet pet)
        {
            StageSpec stage = StageInfo(pet);
            return stage == null ? null : stage.active;
        }
        public float AttackInterval(Pet pet)
        {
            return pet == null ? 0f : Interval(pet) / (1f + (pet.ActiveBuffRemaining > 0f ? pet.ActiveAttackSpeedBonus : 0f));
        }
        public float CombatDamage(Pet pet) { return pet == null ? 0f : Damage(pet) * AuraMultiplier(pet); }
        public float ActiveRange(Pet pet)
        {
            AutoSkillSpec skill = AutoSkill(pet);
            return skill == null ? 0f : Range(pet) * skill.rangeMultiplier;
        }
        private static void ClearActiveBuff(Pet pet)
        {
            pet.ActiveBuffRemaining = pet.ActiveDamageBonus = pet.ActiveAttackSpeedBonus = 0f;
        }
        private void TickAutoClocks(float dt)
        {
            foreach (Pet pet in Towers)
            {
                if (pet.IsEgg || !ValidPad(pet.Pad)) continue;
                pet.ActiveRemaining = Math.Max(0f, pet.ActiveRemaining - dt);
                if (pet.ActiveRemaining < .00001f) pet.ActiveRemaining = 0f;
                pet.ActiveBuffRemaining = Math.Max(0f, pet.ActiveBuffRemaining - dt);
                if (pet.ActiveBuffRemaining < .00001f) ClearActiveBuff(pet);
            }
        }
        private List<Enemy> AutoTargets(Pet caster, AutoSkillSpec skill)
        {
            List<Enemy> targets = new List<Enemy>();
            foreach (Enemy enemy in Enemies)
                if (enemy.Hp > 0f && !targets.Contains(enemy)
                    && Distance(Pads[caster.Pad], new V2(enemy.X, enemy.Y)) <= ActiveRange(caster)) targets.Add(enemy);
            targets.Sort(delegate(Enemy a, Enemy b) {
                if (skill.effect == "assault")
                {
                    float ah = a.MaxHp > 0f ? a.Hp / a.MaxHp : 1f, bh = b.MaxHp > 0f ? b.Hp / b.MaxHp : 1f;
                    int hp = ah.CompareTo(bh); if (hp != 0) return hp;
                }
                int progress = b.Progress.CompareTo(a.Progress);
                return progress != 0 ? progress : a.Id.CompareTo(b.Id);
            });
            return targets;
        }
        private void RunAutoSkills()
        {
            List<Pet> ordered = new List<Pet>(Towers);
            ordered.Sort(CompareUid);
            foreach (Pet caster in ordered)
            {
                AutoSkillSpec skill = AutoSkill(caster);
                if (skill == null || caster.ActiveRemaining > 0f || !ValidPad(caster.Pad)) continue;
                if (skill.effect == "tempo")
                {
                    List<Pet> friends = new List<Pet>();
                    bool engaged = false;
                    foreach (Pet friend in Towers)
                        if (friend != caster && !friend.IsEgg && ValidPad(friend.Pad)
                            && Distance(Pads[caster.Pad], Pads[friend.Pad]) <= ActiveRange(caster))
                        {
                            friends.Add(friend);
                            if (TargetSnapshot(friend, 1).Count != 0) engaged = true;
                        }
                    if (!engaged) continue;
                    ConsumeAuto(caster, skill);
                    foreach (Pet friend in friends)
                    {
                        friend.ActiveDamageBonus = Math.Max(friend.ActiveBuffRemaining > 0f ? friend.ActiveDamageBonus : 0f, skill.allyDamageBonus);
                        friend.ActiveAttackSpeedBonus = Math.Max(friend.ActiveBuffRemaining > 0f ? friend.ActiveAttackSpeedBonus : 0f, skill.allyAttackSpeedBonus);
                        friend.ActiveBuffRemaining = Math.Max(friend.ActiveBuffRemaining, skill.duration);
                    }
                    AutoFx(caster, Pads[caster.Pad], ActiveRange(caster));
                    continue;
                }
                List<Enemy> choices = AutoTargets(caster, skill);
                if (choices.Count == 0) continue; // Ready stays ready, never consumes CD on empty ground.
                V2 center = skill.effect == "quake" ? Pads[caster.Pad] : new V2(choices[0].X, choices[0].Y);
                List<Enemy> victims;
                if (skill.effect == "volley" || skill.effect == "assault")
                {
                    victims = choices;
                    if (victims.Count > skill.targets) victims.RemoveRange(skill.targets, victims.Count - skill.targets);
                }
                else
                {
                    victims = new List<Enemy>();
                    float radius = skill.effect == "quake" ? ActiveRange(caster) : skill.radius;
                    foreach (Enemy enemy in Enemies)
                        if (enemy.Hp > 0f && !victims.Contains(enemy)
                            && Distance(center, new V2(enemy.X, enemy.Y)) <= radius) victims.Add(enemy);
                    victims.Sort(delegate(Enemy a, Enemy b) { return a.Id.CompareTo(b.Id); });
                }
                if (victims.Count == 0) continue;
                ConsumeAuto(caster, skill);
                float damage = CombatDamage(caster);
                if (skill.effect != "volley" && skill.effect != "assault")
                    AutoFx(caster, center, skill.effect == "quake" ? ActiveRange(caster) : skill.radius);
                foreach (Enemy victim in victims)
                {
                    if (!Enemies.Contains(victim) || victim.Hp <= 0f) continue;
                    if (skill.effect == "volley" || skill.effect == "assault") AutoFx(caster, new V2(victim.X, victim.Y), 0f, victim);
                    float hit = damage * skill.damageRatio;
                    if (skill.effect == "quake" && victim.Kind == 3) hit *= skill.eliteMultiplier;
                    if (skill.effect != "volley") hit *= 100f / (100f + Math.Max(0f, victim.Armor));
                    Hurt(victim, hit);
                    if (!Enemies.Contains(victim)) continue;
                    float control = victim.Kind == 3 ? skill.eliteControlMultiplier : 1f;
                    if (skill.effect == "bloom")
                    {
                        float dps = skill.poisonDps * AuraMultiplier(caster);
                        victim.PoisonDps = Math.Max(victim.PoisonRemaining > 0f ? victim.PoisonDps : 0f, dps);
                        victim.PoisonRemaining = Math.Max(victim.PoisonRemaining, skill.duration);
                        if (skill.vulnerability > 0f)
                        {
                            victim.Vulnerability = Math.Max(victim.VulnerableRemaining > 0f ? victim.Vulnerability : 0f, skill.vulnerability);
                            victim.VulnerableRemaining = Math.Max(victim.VulnerableRemaining, skill.duration);
                        }
                    }
                    else if (skill.effect == "frost")
                    {
                        EnemyStatus status = Status(victim);
                        status.Slow = Math.Max(victim.SlowRemaining > 0f ? status.Slow : 0f, skill.slowPower);
                        victim.SlowRemaining = Math.Max(victim.SlowRemaining, skill.duration * control);
                    }
                    else if (skill.effect == "flare")
                    {
                        victim.BurnDps = Math.Max(victim.BurnRemaining > 0f ? victim.BurnDps : 0f, damage * skill.burnRatio);
                        victim.BurnRemaining = Math.Max(victim.BurnRemaining, skill.duration);
                    }
                    else if (skill.effect == "quake")
                        victim.RootRemaining = Math.Max(victim.RootRemaining, skill.rootDuration * control);
                    else if (skill.effect == "assault" && victim.Kind != 3 && victim.Shield <= 0f && victim.Hp > 0f
                        && victim.Hp <= victim.MaxHp * skill.executeThreshold) Defeat(victim);
                }
            }
        }
        private static void ConsumeAuto(Pet pet, AutoSkillSpec skill)
        {
            pet.ActiveRemaining = skill.cooldown;
            pet.AutoCasts++;
        }
        private void AutoFx(Pet pet, V2 target, float radius, Enemy tracked = null)
        {
            Effects.Add(new CombatFx { X = Pads[pet.Pad].X, Y = Pads[pet.Pad].Y, ToX = target.X, ToY = target.Y,
                PetIndex = Spec(pet.Species).atlasIndex, SourceLevel = pet.Level,
                TargetId = tracked == null ? 0 : tracked.Id, TargetKind = tracked == null ? 0 : tracked.Kind,
                TargetSize = tracked == null || EnemyInfo(tracked) == null ? 0 : EnemyInfo(tracked).visualSize,
                Duration = .65f, Life = .65f, IsAuto = true, Radius = radius });
        }
        private void DotHurt(Enemy enemy, float activeTime, float dps)
        {
            // Integrate only the portion of this tick for which vulnerability is active.
            float modifiedTime = activeTime + Math.Min(activeTime, Math.Max(0f, enemy.VulnerableRemaining)) * enemy.Vulnerability;
            if (dps > 0f && modifiedTime > 0f) Hurt(enemy, dps * modifiedTime, false);
        }
        private void TickBurn(Enemy enemy, float dt)
        {
            float activeTime = Math.Min(dt, Math.Max(0f, enemy.BurnRemaining));
            float dps = enemy.BurnDps;
            enemy.BurnRemaining = Math.Max(0f, enemy.BurnRemaining - dt);
            if (enemy.BurnRemaining == 0f) enemy.BurnDps = 0f;
            DotHurt(enemy, activeTime, dps);
        }
        private static void ValidateAutoSkill(PetSpec pet, StageSpec stage)
        {
            AutoSkillSpec skill = stage.active;
            if (skill == null) return;
            string[] effects = { "volley", "bloom", "frost", "flare", "quake", "tempo", "assault" };
            float[] values = { skill.cooldown, skill.initialDelay, skill.damageRatio, skill.rangeMultiplier,
                skill.radius, skill.duration, skill.poisonDps, skill.burnRatio, skill.slowPower, skill.rootDuration,
                skill.vulnerability, skill.allyDamageBonus, skill.allyAttackSpeedBonus, skill.executeThreshold,
                skill.eliteMultiplier, skill.eliteControlMultiplier };
            foreach (float value in values)
                if (!Nonnegative(value)) throw new ArgumentException("自动技能数值必须是有限非负数：" + pet.id, "config");
            if (String.IsNullOrWhiteSpace(skill.name) || String.IsNullOrWhiteSpace(skill.description)
                || Array.IndexOf(effects, skill.effect) < 0 || skill.cooldown < 1f || skill.initialDelay > skill.cooldown
                || skill.damageRatio > 10f || skill.rangeMultiplier <= 0f || skill.rangeMultiplier > 2f
                || skill.targets < 1 || skill.targets > 4 || skill.slowPower > .9f
                || skill.vulnerability > .5f || skill.allyDamageBonus > .5f || skill.allyAttackSpeedBonus > .5f
                || skill.executeThreshold > .25f || skill.eliteMultiplier < 1f || skill.eliteMultiplier > 3f
                || skill.eliteControlMultiplier > 1f
                || !Nonnegative(pet.damage * stage.damageMultiplier * skill.damageRatio * skill.eliteMultiplier)
                || !Nonnegative(pet.damage * stage.damageMultiplier * skill.burnRatio)
                || !Nonnegative(pet.range * stage.rangeMultiplier * skill.rangeMultiplier))
                throw new ArgumentException("自动技能配置无效：" + pet.id, "config");
        }
    }
}
