# CWTD · 森灵守望 · 宠物塔防

v0.5.0：基于18张初版策划截图、8张技能参考与最新明确变更的 Unity 单机原型，工作名称“森灵守望”。引擎：Unity 2022.3.47f1。

## 克隆与构建

```sh
git clone https://github.com/fh1zz/CWTD.git
```

在 Unity Hub 中添加克隆后的 `CWTD` 目录，使用 **Unity 2022.3.47f1** 打开；首次导入会自动生成 Library。打开 `Assets/Scenes/Forestkeepers.unity` 后按 Play。

需要 Windows x64 程序时，先保存当前编辑的场景，再选择 **Forestkeepers → Verify and Build Windows**。此命令会运行核心和绘图检查、重建启动场景并输出 `Builds/Windows/Forestkeepers.exe`。运行时须保留同目录的 `_Data`、`MonoBleedingEdge` 和 DLL 文件。

仓库包含源码、原创AI占位素材、原始策划参考、设计文档和历史验证报告；不包含 Unity 缓存、用户存档、机器设置、旧版备份或 Windows 安装包。报告记录的是对应版本的历史验证结果，不代表每次克隆都会自动执行测试。原始截图仅供设计参考，第三方游戏画面不属于本项目原创资产。

## 先看这里

- [v0.5 画面统一与敌人扩充验证](docs/verification-v05-enemies-ui.md)：消除上下/右侧双地图拼接，12种独立敌人美术、护盾/治疗/分裂/狂暴、分关引入与敌人图鉴。
- [12种敌人设计与数值](design/gdd/v05-enemy-roster.md)：实际倍率、金币、能力和克制关系。

- [地图与数值平衡说明](docs/verification-v04-map-balance.md)：新手绘道路、脚底排序、开局180金币/60抽取、击杀收入与三关165局平衡模拟。

- [宠物自动技能设计](design/gdd/v03-automatic-pet-skills.md)：七宠普攻／被动／自动主动技，35阶配置、CD、目标和叠加规则。
- [v0.3历史验证报告](docs/verification-v03.md)：自动技能的实际测试结果及未完成验收。

- [即抽即用与35阶段设计](design/gdd/v02-direct-pets-five-stages.md)：最新规则、七条进化路线、数值、技能和外形。
- [完整策划入口](design/gdd/systems-index.md)：历史原案与当前变更的区别。
- [后续拓展路线](design/04-expansion-roadmap.md)：待部署栏扩格、养成、能量、长局等；提前孵化方案已取消。
- [v0.2历史报告](docs/verification-v02.md)：上版构建、核心回归和界面状态检查。
- Windows启动程序：本地构建后位于 `Builds/Windows/Forestkeepers.exe`，不纳入 Git。

## 打开与游玩

构建后直接运行 `Builds/Windows/Forestkeepers.exe`；或用 Unity 2022.3.47f1 打开仓库根目录，进入 `Assets/Scenes/Forestkeepers.unity` 后按 Play。

开始守护 → 选关 → 编队 → 出击。初芽林地开放3个编队位，其余两关5个。先选待部署栏中的已有伙伴，再点道路旁“＋”部署。点击开波后，宠物自动攻击。G扭蛋直接得到1阶成熟宠物，可立即部署，无需等待下一波。同种同阶三只可进化，场上主体优先使用池中材料。技能“星雨”先点击按钮再选落点。

Space开波；G扭蛋；E池内批量进化；Esc暂停或取消；右键取消星雨瞄准。暂停菜单可以重试或回到选关。出击只提交实际队伍，编队返回不保存草稿；预设编队有独立保存和启用。

宠物主动技无需手动操作：一阶即可用，首次累计战斗3秒，之后按各自CD自动释放；没有合适目标就等待。选中宠物查看只读CD倒计时，图鉴可切换“普攻·被动／主动技·自动释放”。全局星雨仍为手动指令，与宠物主动技区分。攻击沿用即时命中规则，弹线是短促的命中反馈，不是有延迟伤害的物理弹体。

## 首版已有内容

7种元素宠物、1～5阶、10格待部署栏、3套预设、14个部署点；即抽即用、池内快照合成与场上池优先进化；自动战斗、毒／减速／溅射／光环、全局星雨；金币、出售、回收、暂停、1倍／2倍、胜负与通关／无损／指定元素徽记、本地资料保存。

三关分别6／8／10波，当前共用一张原创手绘森林战场与按画面标定的自然曲线路径，用出怪和数值验证内容梯度，不是三张独立手绘成品关卡。准备阶段显示敌人行进箭头，入口与基地留出显示空间；按统一1.6:1世界比例显示，横向和纵向走位、射程不再拉伸。已限制1～2只一阶宠物挂机通关；持续经营仍可通过，第三关增加压力。长期平衡尚未真人验证。

新增[手绘战场](Assets/Resources/Art/forest-battlefield-v04.png)；战斗仅铺一张底图，不再覆盖旧背景。新增[12种透明敌人图集](Assets/Resources/Art/enemies-v05.png)，已按授权完成本地抠图；战斗顶栏可打开两页敌人图鉴，开波前可预览怪物组合。

美术为原创AI占位：[35阶段宠物图集](Assets/Resources/Art/pet-stages-v2.png)、[宠物与敌人图集](Assets/Resources/Art/units-atlas.png)、[森林背景](Assets/Resources/Art/forest-ground.png)。采用手绘卡通奇幻方向，没有把截图中《王国保卫战》的资源导入游戏。每只宠物的五阶已有独立静态外形、名称、数值和技能成长；编队页可打开“五阶进化图鉴”。这还不是35套逐帧动画。

## 项目结构与继续开发

- Assets/Scripts/Core：独立于Unity的规则程序集。
- Assets/Scripts/PetGame.cs：原型IMGUI表现、输入与资料保存。
- Assets/Scripts/CombatDrawing.cs：统一绘线变换、宠物身体发射点、怪物受击锚点与渐隐；CombatDrawingChecks.cs检查实际Unity矩阵。
- Assets/Resources/balance.json：宠物、关卡、经济和星雨配置；enemyTypes 定义12物种的生命/速度/护甲/金币/能力；关卡 enemyWaves 定义分段出怪名单，bossId 指定末波首领。GameModel.Rules 仅保留旧配置的四类兼容回退。
- Assets/Editor/BuildGame.cs：Forestkeepers菜单 → Verify and Build Windows。
- Assets/Tests/CoreChecks.cs、AutoSkillChecks.cs、TrailChecks.cs、CombatFxChecks.cs、EnemyRosterChecks.cs：确定性核心回归、七宠1→5、自动技、buff与DOT时限、圆滑路线、漏怪边界与弹线事件生命周期。
- design/sources/source-01.png～source-18.png：原始截图归档。
- [v0.2合同](docs/v02-contract.md) / [工作室适配](docs/studio-adaptation.md) / [五阶段美术提示词](docs/art-prompts-v02.md)。

局外资料位于 Unity Application.persistentDataPath 下的 forestkeepers-profile.json（Windows通常在用户AppData/LocalLow/ForestkeepersStudio/Forestkeepers）。只保存预设、出战队伍、最佳挑战徽记和静音；不做中途续战。较新存档只读保护，损坏存档保留备份。

本轮不修改宠物攻击间隔、自动技CD、初始金币、扭蛋价格或清波奖励。敌人能力按游戏时间计时，2倍速会加快现实时间下的战斗。12种造型仍是静态AI占位，不是完整逐帧动画；当前没有新增飞行/隐身敌人或分岔路线。

## 仍未交付为正式功能

现已读取8张伤害／火系技能参考；现有七宠技能为重新设计并标明来源差异，未声称实现截图全部角色、取整公式或能量系统。局外养成、能量、扩格、全七元素专用挑战、拖拽编队、触控／手柄、独立新地图、完整进化／施法动画和正式音频保留在路线图。现有验收用例不能等同于全部执行通过；请以验证报告为准。

最初design/00～03是过程草案，当前以design/gdd和实际验证结果为准。
- [怪物路线修正与验证](docs/verification-v031-path.md)：圆滑弯道、等比例走位、脚底锚点、入口提示和道路避让；三关同步使用。
- [攻击弹线修正与验证](docs/verification-v032-fx.md)：窗口缩放下的绘线矩阵、身体发射点、目标追踪和即时命中反馈。
