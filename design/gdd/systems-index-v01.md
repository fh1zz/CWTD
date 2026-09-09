# 宠物塔防首版 GDD 入口

版本：设计基线 1.0｜日期：2026-09-06｜状态：设计补全稿已交付；本文不主张执行证据，实际构建、核心检查、界面状态与试玩结果见主代理维护的 `docs/verification.md`。

本套文档基于用户实际提供且已阅读的18张截图，以及本轮明确指定的实现接口。工作室 `team-combat` 用于职责划分，所有六份系统 GDD 均按概述、玩家体验、详细规则、公式、边界、依赖、可调参数、验收标准八节组织。

## 阅读顺序与系统登记

| 顺序 | 系统／文件 | 层级 | 内容责任 | 状态 |
|---|---|---|---|---|
| 1 | [核心循环与波次](core-loop-and-waves.md) | Foundation | game-designer、ai-programmer | Draft |
| 2 | [编队与预设](roster-and-presets.md) | Core | game-designer、gameplay-programmer | Draft |
| 3 | [抽蛋、孵化、部署与进化](gacha-incubator-evolution.md) | Core | game-designer、gameplay-programmer | Draft |
| 4 | [战斗与数值](combat-and-balance.md) | Core | game-designer、ai-programmer | Draft |
| 5 | [HUD、暂停与反馈](hud-pause-and-feedback.md) | Presentation | technical-artist、sound-designer、gameplay-programmer | Draft |
| 6 | [结算、挑战与存档](results-and-persistence.md) | Feature | game-designer、gameplay-programmer | Draft |

配套：[来源矩阵](source-matrix.md)、[执行验收用例](acceptance-cases.md)、[team-combat 交接与设计检查](team-combat-handoff.md)、[后续扩展路线](../04-expansion-roadmap.md)。

`Draft` 仅标记本次补全设计的版本状态。各文档定义规则与验收判据；执行证据统一以 `docs/verification.md` 为准。主代理已同步Unity构建成功、核心26组回归及16个界面状态通过，本套文档不据此自行宣称所有AC均已执行。

## 来源、优先级与历史文档

- `SRC-USER01`：真实用户请求：补全现有宠物塔防策划并拓展、随后Unity开发，临时美术参考《王国保卫战》，目标项目F:/TD-game。
- `SRC-I01`～`SRC-I18`：18张已读截图的明确文字与示意布局。完整文件名和规则映射见来源矩阵。
- `EXT-U`：主代理初始补全建议及其方案迭代（不是用户原文）。物种名称、接口与数值均属本轮补全。
- `EXT-A`：本轮实现合同：[技术合同](../../docs/architecture.md)，由主代理提供，不是用户原文；只读，未替换或修改。
- `EXT-C`：本轮实际配置；`EXT-I`：只读核对的核心实现或core角色同步，均不是截图原案。
- `EXT-D`：本次补全的时序、数值候选、交互边界与内容提案。
- `DEFERRED`：后续扩展；不是首版已实现能力。特别是 OB0.2 伤害、技能、能量、下放和教学正文未提供，不能声称已读。

来源优先级：真实用户明确请求 → 18张截图原始逻辑 → 本次EXT补全／技术合同／实现约定。派工和子代理同步属于协作上下文，不可提升为用户原文；现有代码也不能覆盖原案规则。发生修订应记录，不能把较早候选悄悄留作第二份真值。

本轮EXT方案迭代：初始补全建议的12地块改为 **14地块**；准备状态采用 **Wave=0**、购蛋 **ReadyWave=2**；初始补全建议中的星雨100伤害改为实际配置 **110伤害**。这些不是用户截图规定的具体数值。胜负中的漏怪处理保留原文与EXT解释的区别，见结算GDD。

上级目录 `00-source-baseline.md`、`01-design-completion-plan.md`、`02-hud-and-pause.md`、`03-art-direction.md` 是此前仅取得一张截图的历史草案，本轮按派工范围保持不变。凡其中仍写“未读扭蛋／编队”或“必须先读能量才开发”的状态，以本套来源矩阵与首版范围为更新依据。

## 首版不可漏项

| 项目 | 采用约定 | 规则拥有者 |
|---|---|---|
| 引擎／表现 | Unity 2022.3.47f1；Windows；2D正交；原型 IMGUI；参考1920×1080 | 技术合同 |
| 内容 | 7物种、7元素、等级1～5；3关6／8／10波 | 核心循环、战斗 |
| 编队 | 5位可选、教程3位；物种不重复；收藏默认7种解锁 | 编队 |
| 资源 | 开局500金币、20基地HP；抽蛋50；池10格 | 核心循环、孵化 |
| 首波保障 | 实际出战队伍前 min(3,队伍长度) 种各送1只已孵化1级宠物 | 孵化 |
| 部署 | 14固定地块；免费部署；回收不退金币且满池拒绝 | 孵化 |
| 进化 | 三同种同级合一；无金币费；池批量快照；战场主体Uid和Pad保留 | 孵化 |
| 技能 | 星雨110伤害、35秒CD、半径0.26；仅Running；能量留扩展 | 战斗、实际配置 |
| 数据真值 | 后续运行数值以 `Assets/Resources/balance.json` 实际配置为准 | 技术合同 |
| 产品边界 | 单机离线、无局外付费抽卡／商业化、无中途续战 | 结算 |

本GDD角色已只读检查 [实际balance.json](../../Assets/Resources/balance.json)，未修改它；这不代表协作项目尚未运行，实际执行证据见 `docs/verification.md`。实际配置快照、设计推导和未来试验建议分别标记。实现中的固定合同值若偏离上述约定，应作为差异报告修正，不能借“配置为准”覆盖真实用户请求或截图原始逻辑。

## 公共术语与不变量

“回合”和“波”同义；`Wave` 是已开始的最后一波编号（开局0），`ReadyWave` 是蛋允许孵化的波编号；`Preparing` 包括首波前和两波之间，不新增 Intermission 枚举。`Paused` 是正交布尔状态，不能冒充第五个 RunStage。

“物种”是 falcon 等配置ID；“元素”是金等分类；“等级／阶段”统一为 `Level=1..5`，不再额外增加星级或局外等级。“上阵”用于局外编队，“部署／回收”用于局内位置，“出售”才获得金币。

每个宠物Uid在池或战场恰好出现一次；地块最多一只；蛋只在池中；池总量含蛋≤10；所有经济、位置、合成操作原子提交；失败仅更新 LastMessage，不改变金币、实体、随机流、等级、CD和位置。战局结束只锁定一次结果，暂停和结束拒绝所有局内变更。
