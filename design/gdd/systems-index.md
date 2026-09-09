# 宠物塔防 · 当前策划入口

当前版本：v0.3。用户最新要求优先于旧截图：宠物主动技必须自动释放，允许按照现有七宠重新设计。直接出宠与五阶段三合一规则不变。

最新增量先看[七宠自动技能设计](v03-automatic-pet-skills.md)及[本轮验证](../../docs/verification-v03.md)。已读取本轮8张技能／伤害参考，采用CD自动施放而非手动触发，不默认引入能量、复活或改路。

先阅读 [即抽即用与七宠五阶段设计](v02-direct-pets-five-stages.md)。它包含35个阶段的名字、数值、技能、外形、三合一规则、图鉴、迁移、风险和验证要求。

原案三合一仍保留：同种同阶三只合一、最高5阶、单次不连跳、场上主体优先吃待部署栏材料。被取消的是等待一回合孵化，不是三合一。待部署栏10格，扭蛋价格50，仅抽实际编队。

## 当前配套

- [实现合同](../../docs/v02-contract.md) / [实际配置](../../Assets/Resources/balance.json)
- [v0.2验证报告](../../docs/verification-v02.md) / [原创五阶段美术提示词](../../docs/art-prompts-v02.md)
- [来源追溯](source-matrix.md) / [后续路线](../04-expansion-roadmap.md)

## v0.1系统规格留档

下列文档保留原始推导；未变更规则继续适用，但跨回合孵化、阶段不改射程攻速、仅尺寸区分外观等，以v0.2为准。原始截图未被修改。

- [核心循环与波次](core-loop-and-waves.md)
- [编队与预设](roster-and-presets.md)
- [旧抽蛋、孵化与进化](gacha-incubator-evolution.md)
- [旧战斗与数值](combat-and-balance.md)
- [HUD、暂停与反馈](hud-pause-and-feedback.md)
- [结算与存档](results-and-persistence.md)
- [v0.1入口快照](systems-index-v01.md) / [原81条验收判据](acceptance-cases.md)

来源优先级：真实用户最新明确要求→原始截图未被替换的规则→本轮补全。五阶段名称、技能、数值和美术是我们设计的方案，不冒称为用户截图原文。
