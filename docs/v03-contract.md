# v0.3合同：宠物自动主动技

用户要求：8张技能／伤害公式截图供参考，可按现有七宠重新设计；宠物主动技能自动释放，不是手动触发。七宠、五阶、直接扭蛋、三合一均不改。技能分普攻、自动主动技、被动。沿用v0.2普攻及固有效果，称为其被动，不重复叠加同一效果。全局星雨不是宠物技能，保留手动。新数据、技能名字和CD都是本轮补全；不照搬凤凰复活、龙息无限叠层、道路改线、能量系统或局外伤害修正。

## 公开数据与API（必须保持接口一致）

StageSpec增加public AutoSkillSpec active（null即旧版没有主动技能）。PetSpec增加public string basicName,passiveName（仅显示）。
新增[Serializable] AutoSkillSpec:
public string name,effect,description;
public float cooldown,initialDelay,damageRatio,rangeMultiplier,radius,duration,poisonDps,burnRatio,slowPower,rootDuration,vulnerability,allyDamageBonus,allyAttackSpeedBonus,executeThreshold,eliteMultiplier,eliteControlMultiplier;
public int targets;
未使用浮点字段0，rangeMultiplier=1,eliteMultiplier=1,eliteControlMultiplier=1。
合法effect: volley,bloom,frost,flare,quake,tempo,assault。targets1..4；cooldown>=1有限、initialDelay0..cooldown、damageRatio0..10、rangeMultiplier>0且<=2、radius/duration/DPS均非负有限、slowPower0..0.9、vulnerability0..0.5、allyDamageBonus/allyAttackSpeedBonus0..0.5、executeThreshold0..0.25、eliteMultiplier1..3、eliteControlMultiplier0..1。拒绝未知effect、空name/description、配置最终D*ratio/poisonDps溢出。

Pet增加public float ActiveRemaining, ActiveBuffRemaining, ActiveDamageBonus, ActiveAttackSpeedBonus; public int AutoCasts（诊断计数）。新宠ActiveRemaining=本阶initialDelay（配置3秒）。不新增手动宠物施法公开入口。
Enemy增加public float BurnRemaining,BurnDps,RootRemaining,VulnerableRemaining,Vulnerability。消失、死亡清理。
CombatFx增加public bool IsAuto; public float Radius；普通FX默认false。自动技弹道／区域提示可持续0.65秒（表现常量），PetIndex仍基础索引。
新增public AutoSkillSpec AutoSkill(Pet)（非法／无配置返回null）、public float AttackInterval(Pet)（base Interval/(1+ActiveAttackSpeedBonus)，仅有效buff）、public float CombatDamage(Pet)（Damage乘光环与有效tempo增伤）、public float ActiveRange(Pet)（Range*auto.rangeMultiplier，无技能0）。
Damage和Interval保持v0.2无临时buff基础值，避免图鉴基础属性被误改。配置main拥有。

## 生命周期与确定性时序

- 仅Running且未暂停、在战场上的宠物推进ActiveRemaining和ActiveBuffRemaining。池内及准备期冻结，终局冻结。不存储累计“欠发”的技能；ready=0后等待目标，每固定步最多自动施放一次。
- 初次部署要等initialDelay的有效战斗秒，3秒；之后cooldown秒。不因回收、重新部署、开下一波而重置或刷新CD，回收清除自身临时tempo增益（防换位带buff），但ActiveRemaining保留。
- 进化主体保留ActiveRemaining，不凭空就绪；限制到新cooldown上界。消耗材料的CD不转给主体。主体已有tempo增益保留剩余时间，读取新阶段技能。
- 每步：推进宠物主动CD与临时buff→出怪→毒／烧按易伤有效时段积分、随后推进易伤时钟→主动技能（塔UID顺序，先找目标再消费CD）→普攻→移动／漏怪→清波。定身／减速用本步有效时间计算移动后再扣时长，不重复扣时间；技能新增的状态在当步移动即生效。
- 主动技能命中不调用普攻Attack，不递归触发普攻被动。只有下文明确调用的穿透、毒／烧、精英倍率和处决生效。不重置普通攻击CD。普通攻击应用既有被动+新的增益/易伤。
- 敌人唯一快照，判定时仍活着才执行；击杀仍只结算一次。控制对精英持续时间乘eliteControlMultiplier（配置0.5），普通1。定身只停止移动，不改路径，无击退改线。

## 七技语义（每阶实际值由JSON提供）

1 volley / 金隼「曜羽齐射」：ActiveRange内进度desc/Idasc最多targets个不同敌人，各造成CombatDamage*damageRatio真实伤害（无视护甲），仍受目标易伤；不触发普攻多目标或额外被动。
2 bloom / 芽灵「森罗绽放」：ActiveRange内最靠出口敌人作中心，半径radius快照（可命中中心之外射程外敌人）；各受物理直伤D*ratio减甲、施加最终poisonDps（乘施毒瞬间support buff，不再乘阶段damageMultiplier）持续duration；附加vulnerability易伤duration。毒与原芽灵毒共享同物种槽，DPS取强、时间取max。易伤先结算本次直伤再施加，对之后所有伤害生效。
3 frost / 水獭「霜潮涌动」：同样圆形区域，物理直伤+slowPower持续duration，精英持续时间减半；沿用最强慢+max剩余规则。
4 flare / 火狐「九曜焰爆」：同样圆形区域，物理直伤+每秒CombatDamage*burnRatio持续duration的灼烧；灼烧独立于木毒，同类只取最强不叠加、时间max。每帧按有效时间浮点结算，禁止每帧向上取整。
5 quake / 岩龟「山岳震荡」：塔自身为中心、半径ActiveRange，有合法敌人才释放；targets不限制自周围全体。物理直伤，Kind3精英乘active.eliteMultiplier，然后各自减甲；RootRemaining=max(旧,rootDuration*精英控制倍率)。
6 tempo / 光鹿「晨曦协奏」：ActiveRange内其他已部署宠物为候选，不含自身。至少一候选的普攻射程中有敌人才释放；释放给所有候选ActiveDamageBonus/ActiveAttackSpeedBonus+duration（同类最强数值+max剩余时长，不叠加），不回血、不使自身获buff。光鹿永久光环与tempo增伤加算：Damage*(1+max被动光环+有效ActiveDamageBonus)。攻击间隔=阶段Interval/(1+有效ActiveAttackSpeedBonus)。不刷新当前普通攻击Cooldown，只影响下一次加CD的间隔。
7 assault / 暗蝠「夜幕猎袭」：ActiveRange内生命比例升序、进度降序、Id升序最多targets不同敌人，物理伤害D*ratio后按active.executeThreshold处决残血非Kind3（血量>0且<=阈值）；精英免处决，不递归普攻。

易伤：伤害管线末端在Hurt中乘(1+当前有效Vulnerability)，所有直伤、毒、烧、星雨均统一应用一次，不能调用方先乘而Hurt再乘。处决用直接Defeat或专用扣血途径避免重复加成。
已施加DoT的基础DPS在施加瞬间快照support；易伤按tick时目标状态动态应用。旧stages=null/active=null配置全部保持原数值与行为。

## UI（main负责）

35阶段图鉴可切换“普攻·被动 / 自动主动技”查看，避免一格塞全部。编队技能说明分别列三类。局内侧栏显示主动技名、自动释放、剩余秒/就绪待目标/待部署，并给只读进度条；场上选中宠物可有小型只读CD条。不提供点击施法区，不把冷却当能量。自动技FX用现有代码图形（圆环、弹道、技能名短提示）不要求新位图。全局星雨标注“全局指令·手动”。

## 必须覆盖

35个active有效；7技分别在无人输入条件下真实触发；首施与重复CD；缺敌／鹿缺友不消费；无目标ready不攒多发；暂停、准备、回收冻结；重部署与进化不刷新；新buff时限和非叠加；精英控制半时长、处决免疫；AoE每只只受一次；毒+烧独立且死亡奖励一次；易伤只乘一次；首次施放后普攻CD未清零；固定种子与1/60、0.1、长dt分片等价；旧配置回退；原三合一和九次模拟全回归。不得为使测试通过修改生产关卡数值或删除旧测试。
