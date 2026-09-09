# Core verification

Unity 2022.3.47f1 / 2026-09-06 03:56:11Z

```text
核心回归通过：37 组，11907 项断言。
通过：公开合同与纯 System 依赖
通过：归一化路径、14 地块与道路避让
通过：赠送宠物、编队限定与输入复制
通过：池内九合三、无连跳与最高等级
通过：场上主体保留、池材料优先与 UID 顺序
通过：满池和金币不足的原子事务及随机流
通过：部署、占位、回收再部署和宠物蛋限制
通过：出售价格、重复出售与回收无金币
通过：池排序：等级、元素、UID、蛋置后
通过：准备、运行、末波即抽即用与旧方法别名
通过：出怪结束、清场、手动开波与清波奖励
通过：暂停全部计时与恢复同一模拟状态
通过：暂停和终局拒绝全部局内操作
通过：基地归零立即失败且优先于清波
通过：死亡、毒与技能只结算一次奖励
通过：最大路径进度寻敌、同进度 UID 和穿甲
通过：同物种毒取强不叠加、弱毒续时与到期
通过：减速取最强、不相加与精确到期
通过：火狐溅射范围和单次命中
通过：光环范围、伙伴限定和不叠加
通过：重击、疾射频率与等级成长
通过：星雨范围、护甲、冷却和非法落点
通过：三种敌人、末波精英与关卡参数推导
通过：无效时间输入与非法配置
阶段数据：输入实配 35 项；旧配置回退与35项阶段检查均执行，缺省阶段仅用内存测试夹具。
通过：旧阶段回退、图鉴预览与35阶段数据
通过：阶段数组、有限数值和范围错误校验
通过：七物种逐阶三合一与81只一阶合成五阶
通过：穿甲、减速、疾射阶段多目标快照与去重
通过：阶段攻击间隔和射程实际参与战斗
通过：跨阶段毒最终DPS、保强保时与光环快照
通过：跨阶段减速保强保时及到期
通过：阶段溅射半径且targets不复制群伤
通过：阶段光环范围与最强增益
通过：五阶段精英增伤且普通敌人无加成
通过：非精英处决阈值、精英免疫及一次奖励
通过：相同种子重现和时间分片一致
模拟：glade seed=7 Won 波=6 HP=20 击杀=60 金币=209 时长=71.50079秒
模拟：glade seed=19 Won 波=6 HP=20 击杀=60 金币=209 时长=77.30203秒
模拟：glade seed=41 Won 波=6 HP=20 击杀=60 金币=209 时长=70.05048秒
模拟：brook seed=7 Won 波=8 HP=20 击杀=112 金币=220 时长=119.644409秒
模拟：brook seed=19 Won 波=8 HP=20 击杀=112 金币=220 时长=115.0601秒
模拟：brook seed=41 Won 波=8 HP=20 击杀=112 金币=220 时长=120.311218秒
模拟：grove seed=7 Won 波=10 HP=20 击杀=170 金币=256 时长=163.1043秒
模拟：grove seed=19 Won 波=10 HP=20 击杀=170 金币=256 时长=150.424057秒
模拟：grove seed=41 Won 波=10 HP=20 击杀=170 金币=256 时长=161.521347秒
通过：三关九次普通策略模拟及完整胜败
自动技能专项通过：11组，347项断言。
通过：自动技 35配置与七技无人输入真实触发
通过：自动技 首施CD、无目标保留、循环间隔
通过：自动技 暂停、准备、回收重部署、进化
通过：自动技 金隼真实伤害、多目标不重复
通过：自动技 木毒、易伤与火烧独立
通过：自动技 霜潮精英半时长、定身精确移动
通过：自动技 光鹿增益条件、叠加上限与到期
通过：自动技 暗蝠低血优先、处决免疫和奖励一次
通过：自动技 自动释放不重置普攻、无主动被动递归
通过：自动技 非法配置与旧active回退
通过：自动技 时间分片确定性

Trail length=2.7786 max tangent step=9.53deg; ordinary traversal=32.69s
PASS trail: continuous bounded bends, no crossings or zero-length segments
Minimum pad-centre clearance=85.8px at reference resolution
PASS trail: pad clearance and useful first-stage road coverage
PASS trail: arc-distance sampling and exact entry/exit boundaries
PASS trail: short/duplicate corners, custom routes and deterministic generation
PASS trail: all enemy speeds follow road, pause/root freeze, leak once
Trail groups=5 assertions=4416

Combat FX groups=5 assertions=281
PASS combat FX: seven pets, five stages, correct source/target snapshots and instant hit
PASS combat FX: moving target ID, removal and frozen last location without retargeting
PASS combat FX: FX life, pause and visual events never deal additional damage
PASS combat FX: automatic volley/assault track enemies; area/support remains on ground
PASS combat FX: star rain has real ground radius and no phantom target

```
