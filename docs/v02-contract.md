# v0.2 实现合同：即抽即用与五阶段

真实用户最新要求：1.扭蛋机直接孵化出宠物；2.每个宠物可升级，共5阶段，由我们设计。下述数值／命名／技能是本轮设计，不是用户给定。

升级方式保留原案三只同Species同Level合一、每次只升一级、池批量快照、场上主体保留且池材料优先。最高5阶。抽取价格／编队池／容量不变，抽到1阶成熟宠物直接入待部署栏，ReadyWave=0；DrawEgg保留作为兼容方法别名，可新增DrawPet，UI使用DrawPet。不再有跨回合孵化或末波废蛋。New game无蛋，旧字段仅保留兼容核心检查构造数据，不延伸为在玩功能。

## 数据合同（namespace PetTD）

新增Serializable StageSpec：public string name,ability,description,appearance；public float damageMultiplier,rangeMultiplier,intervalMultiplier,effectPower,effectDuration,executeThreshold,eliteMultiplier；public int targets。

PetSpec新增public StageSpec[] stages；恰好5项，Stage1..5按数组索引0..4。实际配置由主代理写入balance.json。核心新增public StageSpec StageInfo(Pet)、public float Interval(Pet)、public bool DrawPet()，其余公开接口与实体字段保持。StageInfo非法pet应返回null而不越界。对旧stages=null配置采用原damageScale／range／interval／effectPower的兼容行为；新配置严格校验五项及有限数值、targets1..3、executeThreshold0..0.25、effectDuration非负、eliteMultiplier>=1。

Damage=PetSpec.damage*StageSpec.damageMultiplier；Range=PetSpec.range*rangeMultiplier；Interval=PetSpec.interval*intervalMultiplier。无阶段数据按旧逻辑计算。伤害、范围、攻速、技能必须共用StageInfo，不能UI独自放大。

## 七类技能

- armor：effectPower为忽略护甲比例；targets个不同目标，按进度降序、Id升序，从攻击开始时合法目标快照选择，每个独立一次伤害与弹道，不允许重复命中同一目标。
- poison：effectPower就是最终阶段基础毒DPS，不再额外乘damageMultiplier；受施毒瞬间光环影响。effectDuration持续秒数。同物种毒取强不叠加，刷新使用max(原剩余时长,新持续时长)，避免弱毒缩短强毒。
- slow：effectPower减速比例，effectDuration秒；取强不叠加，刷新时max剩余时长；targets为同时攻击的不同目标数。
- splash：effectPower为溅射半径；主目标与半径内其他目标各结算一次，不使用targets复制群伤。
- heavy：仅对Kind3精英本次直伤乘eliteMultiplier，不触发眩晕、击退、百分比伤害。
- aura：effectPower为对其他塔的增伤比例，范围读取支持塔的Range；多个光环取最强不叠加，不自增伤。
- rapid：快速攻击；targets为同时攻击的不同目标数；对非Kind3目标，正常伤害后若0<Hp<=MaxHp*executeThreshold，处决一次；精英免处决，不能重复金币。

效果颜色和身份仍使用基础atlasIndex0..6；角色图使用新5列7行图集，行按基础atlasIndex，列按Level-1。实际UV使用Art/pet-stage-regions.json中的35个像素区域，以防生成图片非严格等间距；不缩放源位图，保持各精灵宽高比，底部中心对齐。旧12格图集保留给敌人和扭蛋机。

## UI与验证

“孵化池”统一改“待部署栏”，去除等下一波、蛋ReadyWave、末波不能孵化、破壳计时。提示即抽即用。编队添加五阶图鉴，展示每个阶段原创外形／名字／当前攻击／范围／攻速／技能与3合1成本，末阶标最高。局内侧栏显示真实阶段名与技能，进化按钮与当前条件一致。

验证必须覆盖准备／运行／末波直接出成熟1阶宠物且立即可部署、队伍限定、金钱与容量事务、七物种各1→5、最高阶禁止再升、快照不连跳、穿甲与多目标去重、毒与减速跨阶段、光环取强、精英增伤、非精英处决且精英免疫、暂停全冻结、存档编队兼容。原9次通关模拟重跑，偏易只记录不擅改敌人。
