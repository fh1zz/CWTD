using System;
using System.Collections.Generic;

namespace PetTD
{
    [Serializable]
    public class EnemySpec
    {
        public string id, name, role, description, counter, behavior, splitId;
        public int kind, atlasIndex, reward, leak, splitCount;
        public float hpMultiplier, speedMultiplier, armor, shieldRatio, visualSize;
        public float abilityInterval, abilityRadius, healRatio, splitHpRatio;
        public float rageThreshold, rageSpeedMultiplier;
    }

    [Serializable]
    public class EnemyWave
    {
        public int firstWave;
        public string[] roster;
    }

    public partial class GameModel
    {
        public EnemySpec EnemyInfo(Enemy enemy)
        {
            return enemy == null ? null : EnemyInfo(enemy.Species);
        }

        public EnemySpec EnemyInfo(string id)
        {
            if (Config.enemyTypes != null)
                foreach (EnemySpec spec in Config.enemyTypes) if (spec.id == id) return spec;
            return null;
        }

        // Read-only: the preview and actual spawn use the same deterministic roster.
        public string[] WaveRoster(int wave)
        {
            if (Level.enemyWaves == null) return new string[0];
            EnemyWave band = null;
            foreach (EnemyWave item in Level.enemyWaves)
                if (item.firstWave <= wave) band = item;
            if (band == null) return new string[0];
            int count = Level.baseCount + Level.countGrowth * (wave - 1);
            string[] result = new string[count];
            for (int i = 0; i < count; i++) result[i] = band.roster[i % band.roster.Length];
            if (wave == Level.waves && !String.IsNullOrEmpty(Level.bossId)) result[count - 1] = Level.bossId;
            return result;
        }

        private Enemy CreateEnemy(EnemySpec spec, float hp, float progress, int generation = 0)
        {
            Enemy enemy = new Enemy { Id = nextEnemyId++, Kind = spec.kind, Species = spec.id,
                Hp = hp, MaxHp = hp, Armor = spec.armor, Progress = progress,
                Speed = (Config.enemyBaseSpeed > 0 ? Config.enemyBaseSpeed : Rules.BaseSpeed)
                    * Level.speedScale * spec.speedMultiplier,
                Reward = generation > 0 ? 0 : spec.reward, Leak = spec.leak,
                Shield = hp * spec.shieldRatio, MaxShield = hp * spec.shieldRatio,
                AbilityRemaining = spec.abilityInterval, Generation = generation };
            PlaceOnPath(enemy);
            return enemy;
        }

        private void TickEnemyAbilities(float dt)
        {
            foreach (Enemy enemy in Enemies.ToArray())
            {
                enemy.PulseRemaining = Math.Max(0, enemy.PulseRemaining - dt);
                EnemySpec spec = EnemyInfo(enemy);
                if (spec == null || enemy.Hp <= 0) continue;
                if (spec.rageThreshold > 0 && !enemy.Enraged && enemy.Hp <= enemy.MaxHp * spec.rageThreshold)
                {
                    enemy.Enraged = true;
                    enemy.Speed *= spec.rageSpeedMultiplier;
                    enemy.PulseRemaining = .65f;
                }
                if (spec.behavior != "heal") continue;
                enemy.AbilityRemaining = Math.Max(0, enemy.AbilityRemaining - dt);
                if (enemy.AbilityRemaining > 0 || enemy.RootRemaining > 0) continue;
                Enemy target = null;
                foreach (Enemy other in Enemies)
                {
                    if (other == enemy || other.Hp <= 0 || other.Hp >= other.MaxHp
                        || Distance(new V2(enemy.X, enemy.Y), new V2(other.X, other.Y)) > spec.abilityRadius) continue;
                    if (target == null || other.Hp / other.MaxHp < target.Hp / target.MaxHp
                        || (other.Hp / other.MaxHp == target.Hp / target.MaxHp && other.Id < target.Id)) target = other;
                }
                if (target == null) continue; // Ready, not banked; one heal when a valid ally appears.
                target.Hp = Math.Min(target.MaxHp, target.Hp + target.MaxHp * spec.healRatio);
                enemy.AbilityRemaining = spec.abilityInterval;
                enemy.PulseRemaining = target.PulseRemaining = .55f;
            }
        }

        private void SpawnChildren(Enemy parent)
        {
            EnemySpec spec = EnemyInfo(parent);
            if (spec == null || spec.behavior != "split" || parent.Generation > 0) return;
            EnemySpec child = EnemyInfo(spec.splitId);
            for (int i = 0; i < spec.splitCount; i++)
            {
                // Same road, slightly behind parent. No teleport forward, no recursive
                // splitting, no extra gold. Children must die/leak before wave clear.
                Enemy spawnedChild = CreateEnemy(child, parent.MaxHp * spec.splitHpRatio,
                    Math.Max(0, parent.Progress - .018f * i), parent.Generation + 1);
                spawnedChild.PulseRemaining = .45f;
                Enemies.Add(spawnedChild);
            }
        }

        private static void ValidateEnemyRoster(GameConfig config, LevelSpec level)
        {
            if (config.enemyTypes == null)
            {
                if (level.enemyWaves != null || !String.IsNullOrEmpty(level.bossId))
                    throw new ArgumentException("敌人波次缺少物种表。");
                return;
            }
            var byId = new Dictionary<string, EnemySpec>(StringComparer.Ordinal);
            foreach (EnemySpec spec in config.enemyTypes)
            {
                if (spec == null || String.IsNullOrWhiteSpace(spec.id) || byId.ContainsKey(spec.id)
                    || String.IsNullOrWhiteSpace(spec.name) || String.IsNullOrWhiteSpace(spec.role)
                    || spec.kind < 0 || spec.kind > 3 || spec.atlasIndex < 0 || spec.atlasIndex >= 12
                    || spec.reward < 0 || spec.leak < 1 || !Positive(spec.hpMultiplier) || !Positive(spec.speedMultiplier)
                    || !Nonnegative(spec.armor) || !Nonnegative(spec.visualSize) || spec.visualSize > 120
                    || !Nonnegative(spec.shieldRatio) || spec.shieldRatio > 1
                    || !Nonnegative(spec.abilityInterval) || !Nonnegative(spec.abilityRadius)
                    || !Nonnegative(spec.healRatio) || spec.healRatio > .2f || !Nonnegative(spec.splitHpRatio)
                    || !Nonnegative(spec.rageThreshold) || spec.rageThreshold > .5f || !Nonnegative(spec.rageSpeedMultiplier)
                    || Array.IndexOf(new[] { "none", "shield", "heal", "split", "rage" }, spec.behavior) < 0)
                    throw new ArgumentException("敌人物种配置无效。");
                if (spec.behavior == "heal" && (spec.abilityInterval < 2 || spec.abilityRadius <= 0 || spec.healRatio <= 0)
                    || spec.rageThreshold > 0 && (spec.rageSpeedMultiplier <= 1 || spec.rageSpeedMultiplier > 2)
                    || spec.behavior == "split" && (spec.splitCount < 1 || spec.splitCount > 3 || spec.splitHpRatio <= 0 || spec.splitHpRatio > .5f))
                    throw new ArgumentException("敌人能力超出安全范围。");
                byId.Add(spec.id, spec);
            }
            foreach (EnemySpec spec in config.enemyTypes)
                if (spec.behavior == "split" && (String.IsNullOrEmpty(spec.splitId) || !byId.ContainsKey(spec.splitId)
                    || byId[spec.splitId].behavior == "split" || byId[spec.splitId].reward != 0))
                    throw new ArgumentException("分裂幼体必须有效、不可连锁且零金币。");
            if (level.enemyWaves == null) return; // Legacy/isolated fixture spawn cycle remains supported.
            if (level.enemyWaves.Length == 0 || level.enemyWaves[0] == null || level.enemyWaves[0].firstWave != 1)
                throw new ArgumentException("敌人波次必须从第一波开始。");
            int previous = 0;
            foreach (EnemyWave band in level.enemyWaves)
            {
                if (band == null || band.firstWave <= previous || band.roster == null || band.roster.Length == 0)
                    throw new ArgumentException("敌人波次顺序或名单无效。");
                previous = band.firstWave;
                foreach (string id in band.roster)
                    if (id == null || !byId.ContainsKey(id)) throw new ArgumentException("敌人波次引用未知物种。");
            }
            if (!String.IsNullOrEmpty(level.bossId) && (!byId.ContainsKey(level.bossId) || byId[level.bossId].kind != 3))
                throw new ArgumentException("关卡首领必须是精英物种。");
        }
    }
}
