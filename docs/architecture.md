# Unity 首版架构与接口合同

> v0.2更新：即时抽宠、StageSpec与五阶段技能以 [v02合同](v02-contract.md) 为当前权威。本文件以下内容为v0.1架构留档，其中ReadyWave等待孵化、固定射程／攻速、统一幂次伤害已被取代。

2026-09-06；技术方案采用 Unity 2022.3.47f1、C#、2D 正交表现、Windows 独立构建。设计补全和实施由用户整体授权。核心循环以18张原始截图为准，其余采用明确标记的EXT规则。

## 分层

- `Assets/Scripts/Core/GameModel.cs`：不依赖 Unity 的可重现模拟、抽取、孵化、进化、经济与胜负。随机使用显式 seed。
- `Assets/Scripts/PetGame.cs`：Unity 入口、画面、输入、菜单、本地存档；只能调用模拟接口改变战局。
- `Assets/Resources/balance.json`：宠物、关卡、经济和星雨参数。敌人类型倍率、毒／减速时长等集中在GameModel.Rules；这些固定量尚未外置，不把JSON误称为全部参数。
- `Assets/Resources/Art`：原创占位位图。游戏代码不依赖图片中的文字、道路或单位。
- `Assets/Editor/BuildGame.cs`：生成启动场景、验证、Windows构建。不得通过修改全局机器配置或绕过许可证来启动。
- `Assets/Tests/CoreChecks.cs`：可调用的确定性核心规则回归测试，不需要外部测试包。

场景引导创建唯一 PetGame 实例。UI 使用 Unity IMGUI 自定义绘制与交互（首版原型方案），1920×1080参考坐标适配，战场模拟坐标与屏幕布局解耦。后续可替换为UGUI而不更改核心。

## 首版内容边界

7种宠物、等级1～5、5编队槽、10孵化槽、3套预设、3关（6/8/10波），前3个队伍物种各赠送1只可部署宠物。抽取均匀且仅限当前编队，50金币，初始500金币/20HP。一次进化消耗3只同物种同等级，仅提升一级；池批量以按钮点击前快照分组。主动星雨是EXT补全，不代表读取了用户尚未提供的能量/技能章节。能量与长期养成暂留扩展规格。

## 核心公开合同（namespace PetTD）

所有类型位于 `GameModel.cs`，供表现和检查共同引用。数值字段可由 JSON 读取。

```csharp
[Serializable] public class GameConfig {
 public int initialGold, initialLives, drawCost, poolCapacity, maxLevel;
 public int sellBase, clearReward, clearRewardGrowth;
 public float levelDamageScale, skillCooldown, skillDamage, skillRadius;
 public PetSpec[] pets; public LevelSpec[] levels;
}
[Serializable] public class PetSpec {
 public string id,name,element,role,description,colorHex,effect;
 public float damage,range,interval,effectPower;
 public int atlasIndex;
}
[Serializable] public class LevelSpec {
 public string id,name,subtitle;
 public int waves,slots,baseCount,countGrowth;
 public float hpScale,speedScale,baseHp,hpGrowth,spawnInterval;
}
public struct V2 { public float X,Y; public V2(float x,float y); }
public enum RunStage { Preparing, Running, Won, Lost }
public class Pet {
 public int Uid,Level,ReadyWave,Pad; public string Species;
 public float Cooldown; public bool IsEgg {get;}
}
public class Enemy {
 public int Id,Kind,Reward,Leak; public float X,Y,Progress,Hp,MaxHp,Speed,Armor;
 public float SlowRemaining,PoisonRemaining,PoisonDps;
}
public class CombatFx { public float X,Y,ToX,ToY,Life,Duration; public int PetIndex; }
public class GameModel {
 public GameConfig Config; public LevelSpec Level;
 public List<Pet> Pool,Towers; public List<Enemy> Enemies; public List<CombatFx> Effects;
 public List<V2> Path,Pads; public int Coins,Lives,Wave,Kills;
 public RunStage Stage; public bool Paused;
 public float Elapsed,SkillRemaining; public string LastMessage;
 public GameModel(GameConfig config,int levelIndex,string[] team,int seed);
 public PetSpec Spec(string species);
 public bool DrawEgg(); public bool StartWave(); public void Tick(float dt);
 public bool Deploy(int uid,int pad); public bool Recall(int uid);
 public bool Sell(int uid); public int SellValue(Pet pet);
 public bool CanEvolve(int uid); public bool EvolveTower(int uid);
 public int EvolvePool(); public bool CanEvolvePool();
 public bool CastSkill(float x,float y);
 public float Damage(Pet pet); public float Range(Pet pet);
 public Pet FindPet(int uid); public int RemainingToSpawn {get;}
 public float Distance(V2 a,V2 b);
}
```

所有失败方法返回 false（批量进化返回0），设置本地化中文 LastMessage；不得部分扣款、删除或产生副作用。Paused/Won/Lost拒绝全部局内经济与战斗操作。启动下一波只能从Preparing调用。

## 坐标、时钟与回合

- Path/Pads 使用0..1归一化坐标。战场约1.6宽高比，Distance使用sqrt((dx*1.6)^2+dy^2)。路径Progress使用此距离单位，Speed为每秒路径距离。
- 给出14个固定合法Pad坐标，避开道路；视觉绘制必须完全复用它们。Path从左侧进入绕行到右上出口。
- Tick接收已按运行倍率调整的dt；Paused时返回且不推进任何效果。UI计时独立。
- Wave初始0；赠送3只准备期宠物保障首波。准备期购买蛋 ReadyWave=2；第N>=1波及其准备期购买蛋 ReadyWave=N+1。下一波StartWave时孵化ReadyWave<=Wave的蛋。
- 出怪结束且存活敌人为空才清波。最后一波清空且Lives>0胜利。Lives<=0立即失败，无需仍存在敌人。
- 回合过渡由玩家手动开波，防止低熟练玩家没有部署时间。
- 出售金额floor(sellBase*3^(level-1))，sellBase=20；蛋不可出售、部署或进化。回收不产生金币，池满失败。
- 池排序：等级降序→金木水火土光暗→Uid；蛋在最后并按Uid稳定排序。进化批处理不能消费刚产生的新等级宠物。
- 战场进化固定被选中的主体Uid和Pad，材料优先池中同级同种，再按Uid使用其他塔。主体CD恢复为当前值与新CD较小者，不凭空重置技能收益。

## 宠物与战斗补全

7个宠物id/元素/效果：falcon金(armor)、sprout木(poison)、otter水(slow)、fox火(splash)、turtle土(heavy)、deer光(aura)、bat暗(rapid)。各角色参数在JSON。寻敌优先距出口最近，即最大Progress；伤害立即结算并绘制短暂弹道，避免无效追踪对象。物理护甲减伤=100/(100+armor)，金隼忽略一部分护甲；减速取最强不相加、刷新持续时间；毒同源刷新；火狐溅射有限半径；光鹿范围支援不叠加；土龟基础高伤、暗蝠快速攻击。

技能星雨：点击技能后在地图选择位置，范围伤害；CD由Config配置，初始可用，取消不消耗。仅Running允许施放。

## 存档与交付

保存局外3预设、已启用预设索引、上次实际出战队伍、关卡最好评级、静音设置；阵容编辑返回不提交。JSON使用版本字段，读取校验长度/物种ID/重复/索引，写入临时文件后替换。本局不做中断续战，失败和重试不重复提交奖励。

核心测试直接创建config和模型；覆盖事务、回合孵化、9合3不连跳、跨池材料优先、暂停、失败优先、编队池约束、确定性与批量模拟。Unity编译/构建和真实画面截图分别记录，不能用一类证据替代另一类。
