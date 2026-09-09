# team-combat 设计交接与检查记录

日期：2026-09-06。本文件记录策划子代理的设计交接，不代表所有实现和验收完成。实际使用工作室 `team-combat` 分工，由策划、核心程序、技术审查三个子代理与主代理协作；主代理集成源码、美术与构建，执行证据见 [验证报告](../../docs/verification.md)。真实用户授权补全策划与开发，模板的逐文件确认不再适用，但派工数值不能变成用户原文。

## 分工与产物

| 工作室角色 | 本轮设计责任／产物 | 后续实施责任 |
|---|---|---|
| game-designer | 六系统GDD、核心体验、来源差异、公式单位、边界和体验目标 | 与实际试玩一起调参并维护规则 |
| gameplay-programmer | 原子操作语义、Uid归属、状态机、字段／方法交接、编队和存档提交时点 | 按只读architecture.md实现GameModel与PetGame |
| ai-programmer | 基础敌人路径、Progress寻敌、生成／清波、同帧死亡和漏怪顺序 | 实现与验证敌人运动和目标选择 |
| technical-artist | HUD相对位置、选择／进化／范围反馈、原创手绘占位规格 | 主代理已制作原创占位并集成；最终效果以验证报告为准 |
| sound-designer | 音频事件、并发、重要提示优先级、暂停冻结约定 | 后续制作和接入音频；本轮未制作 |
| unity-specialist | 以已给合同固定Unity2022.3.47f1、IMGUI原型、归一化坐标、纯模型/表现隔离 | 后续版本适配、编译、运行检查；本轮不检验API或替换技术栈 |
| qa-tester | 81条执行验收用例及证据边界；来源覆盖与公式验算 | 后续执行CoreChecks、交互和实机试玩，不以设计稿充当PASS |

## 合同兼容性

原架构 [docs/architecture.md](../../docs/architecture.md) 与实际 [balance.json](../../Assets/Resources/balance.json) 均只读。不存在本轮新建的第二套核心类型、网络层或能量层。以下是对已有类型的语义补足，不是新增代码。

| 已有接口／字段 | GDD确定的输入输出与约束 |
|---|---|
| GameModel(config,levelIndex,team,seed) | 配置合法、关卡合法、team1..slots种且不重复；生成500/20/Wave0/Preparing及至多3赠宠；非法内容进入UI错误流程而非半初始化战局 |
| RunStage / Paused | 仅Preparing、Running、Won、Lost；Paused布尔正交；不新增Intermission/Spawning/Ended枚举 |
| Wave / Pet.ReadyWave / IsEgg | Wave0准备；ReadyWave=max(2,Wave+1)，StartWave递增后孵化；ReadyWave>0为蛋，孵化归0 |
| Pool / Towers / Pet.Pad | 单Uid单归属；池Pad=-1；场上Pad0..13；同Pad最多一只 |
| DrawEgg() | 成功一次扣50并生成1蛋返回true；失败false、LastMessage；随机流不因失败前进 |
| StartWave() / RemainingToSpawn | 仅Preparing，原子波号递增、孵化、建立出怪队列；剩余0不等于清场 |
| Deploy(uid,pad) / Recall(uid) | 免费迁移同Uid；Recall满池失败，无金币回收；蛋拒绝 |
| Sell(uid) / SellValue(pet) | 已孵实例20×3^(Level-1)向下取整，出售删除一次；蛋卖价查询0、Sell仍失败 |
| CanEvolve(uid) / EvolveTower(uid) | 塔主体＋优先池的两材料，主体Uid／Pad不变，CD=min(当前,新interval)；每次仅升1阶 |
| CanEvolvePool() / EvolvePool() | 只使用池原快照；返回产物个数；0为无变化失败，不返回耗材数 |
| Damage(pet) / Range(pet) | 分别返回damage×2.05^(Level-1)的基础伤害及配置range；光环和目标护甲在实际攻击结算时应用 |
| CastSkill(x,y) | Running、可用CD、合法归一化战场位置才成功；110无视护甲AoE、35秒CD、0.26半径；空放合法；取消由UI处理不调用 |
| Spec(species) / FindPet(uid) | 成功返回匹配配置／实例；无匹配查询返回空，由调用方退出该操作，不替换成首个物种 |
| Distance(V2,V2) / Path / Pads | sqrt((1.6dx)^2+dy^2)，Path/Pads按核心GDD坐标统一生成与绘制 |
| Enemy.PoisonRemaining/PoisonDps | 单一木毒状态，较强DPS刷新3秒；不伪造未存储的独立源列表 |
| Enemy.SlowRemaining | 首版只有水宠固定30%比例，2秒刷新；将来不同强度需要扩展数据合同 |
| CombatFx | 纯表现，Life/Duration暂停冻结；画面弹道不会延迟或重复伤害 |
| Tick(dt) / Elapsed / SkillRemaining | dt已按倍率调整；Preparing不推进战斗计时；Paused/终局无推进；无效dt不改变状态 |
| LastMessage | 失败中文原因，可变化；其余状态无副作用；由失败事件触发UI单例2秒提示 |

池批量進化按钮的查询与战场主体的查询范围不同，不能以CanEvolve(uid)替代CanEvolvePool。所有选择、折叠、当前界面、阵容编辑、启用预设、静音与成绩放表现／局外层，不要求往GameModel强加字段。

## 配置归属与未有字段的处理

六份GDD引用同一实际配置，不复制新JSON。核心拥有LevelSpec、initialGold/initialLives、clearReward；孵化拥有drawCost/poolCapacity/maxLevel/sellBase；战斗拥有PetSpec和levelDamageScale/skillCooldown/skillDamage/skillRadius。跨系统引用不算重复拥有可调值。

当前配置没有敌人种类数组、独立毒／减速时长、关卡挑战数组、局外养成表。首版采用Core集中EXT常量中的Kind0/1/2及末波Kind3，具体参数与坐标在核心循环GDD对齐；毒3秒／减速2秒与配置描述一致；挑战使用稳定Level.id的明确映射。若开发希望独立调这些项，应后续增加数据字段并同步合同，不能声称原JSON已经有完整敌人／能量配置。本轮不增加字段、不修改合同。

## 设计交叉检查

检查方式：主会话单文档完整性与跨系统一致性检查，参考工作室design-review的solo检查范围；不是独立多代理评审。GDD均有按顺序8节；所有系统间依赖双向列明，来源截图、真实用户请求与本轮补全建议分别可追溯。

| 跨系统场景 | 采用的唯一处理 | 对应验收 |
|---|---|---|
| Wave0买蛋→开1→准备→开2 | 开1不孵化；开2孵化并原Uid排序 | AC-G03/L02/L03 |
| 满池／缺币快速重复抽取 | 提交前复核，失败只改LastMessage，不消费随机或钱 | AC-G01/G02/U06/U07 |
| 9只1级池进化 | 快照→3只2级；第二次点击才可升3 | AC-G11/G12 |
| 场上进化材料跨两个容器 | 池优先、塔按Uid、主体位置固定、CD=min、选择清空 | AC-G13～G16 |
| 暂停时毒、CD、出怪、弹道同时存在 | 模型全部冻结，菜单可交互，不补真实停留时间 | AC-U08～U12 |
| 最后一敌漏怪归零 | Lost优先；空敌人列表不反转胜负 | AC-S02/S04 |
| 5槽预设进3槽教程 | 按前N截断，实际队伍成为抽取池与赠宠来源 | AC-R02/G06 |
| 全元素挑战不可达 | 保留来源、后续启用，首版不投放 | AC-S06 |
| 光环＋毒＋移除主体 | 攻击读当前光环、毒存施加快照、删除塔不回滚旧伤害 | AC-B04～B07 |

设计检查结论：核心源规则与公开接口已有可执行定义；数值和实际通关体验仍待开发后验证。当前核心已有普通／快速／护甲和末波精英；规则验算不能替代真人试玩对战术收益的检验；4～5级在短关中通常不可自然达到；这些是已登记的内容／平衡限制，不伪装成已验证体验。

## 后续按“先设计后实现”顺序交接

1. 本轮完成六份GDD、来源、验收与路线，停止在文档交付。
2. 后续gameplay-programmer/ai-programmer依据合同实现模型，先验证抽蛋与回合孵化、材料优先与快照、全冻结和胜负。
3. 表现层接入编队／预设、HUD与十四点，再接入原创占位美术、特效和声音。
4. 核心检查、Unity编译构建、真实交互截图分别留证，试玩后调整单一参数组。

GDD职责状态：设计文档完成，本角色未编辑代码或生成资源。主代理已同步Unity构建成功、核心26组回归及16个界面状态通过；项目执行证据统一由 `docs/verification.md` 记录，本文件不笼统称项目未实施，也不替该报告扩张通过范围。只在派工指定GDD目录和扩展路线文件内交付，没有修改架构或配置。
