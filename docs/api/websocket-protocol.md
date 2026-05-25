# WebSocket API 协议规范

本文档描述当前服务端实际实现的 WebSocket 协议，并与以下代码保持一致：

- `server/src/thuai/Protocol/Messages/PerformMessages.cs`
- `server/src/thuai/Protocol/Messages/BroadcastMessages.cs`
- `server/src/thuai/Program.cs`

## 概述

- 默认地址：`ws://localhost:14514`
- 路径：根路径直连
- 数据格式：JSON
- 字段命名：camelCase
- 分发字段：`messageType`
- `null` 字段：服务端序列化时会忽略

当前实现支持 `player`、`observer` 和 `admin` 三种角色，支持显式 `HELLO` 握手。PlayerID 为 server 分配的整数标识，用于指代选手，避免 TOKEN 泄露。

## 实现状态

| 能力 | 当前状态 | 说明 |
| --- | --- | --- |
| Player 动作消息 | 已实现 | `LIMIT_BUY`、`LIMIT_SELL`、`CANCEL_ORDER`、`SUBMIT_REPORT`、`SELECT_STRATEGY`、`ACTIVATE_SKILL` |
| `HELLO` 握手 | 已实现 | 支持 player / observer / admin 三种角色 |
| Socket 懒绑定 | 已实现 | 第一条带 `token` 的动作消息也会绑定 socket（兼容旧版） |
| `GAME_STATE` | 已发送 | 每个 game tick 广播 |
| `MARKET_STATE` | 已发送 | `TradingDay` 和 `Settlement` 阶段广播 |
| `PLAYER_STATE` | 已发送 | 私发对应 player，包含 `playerId` |
| `STRATEGY_OPTIONS` | 已发送 | `StrategySelection` 阶段广播 |
| `NEWS_BROADCAST` | 已发送 | 公共新闻、伪造新闻、内幕预览共用同一 schema |
| `REPORT_RESULT` | 已发送 | 仅发送给提交该研报的 player |
| `TRADE_NOTIFICATION` | 已发送 | 成交时分别发送给买方和卖方 |
| `SKILL_EFFECT` | 已发送 | 技能触发后广播给所有 player |
| `DAY_SETTLEMENT` | 已发送 | 月结算广播 |
| `ERROR` | 已发送 | 校验错误等 |
| Observer 连接 | 已实现 | 接收公共广播和未伪造新闻 |
| Admin 连接 | 已实现 | 支持 DEBUG 系列命令 |
| `PLAYER_SUMMARY_STATE` | 已发送 | Observer 接收的选手摘要 |

## 连接生命周期

当前实现的连接流程：

1. Client 建立 WebSocket。
2. Client 发送 `HELLO` 消息（player 角色需带 `token`）。
3. Server 绑定 socket。通过 `PLAYER_STATE` 返回分配的 `playerId`。
4. 之后在下一次服务端 tick 广播时开始收到快照和事件消息。
5. Client 断线重连后，需重新发送 `HELLO`。

说明：

- 当前没有 `HELLO`。
- 当前没有 `HELLO_ACK`。
- 当前没有“连接后立即补发完整快照”的逻辑；是否马上收到消息取决于下一次服务端 tick。

## 通用字段语义

- `token`：选手标识。
- `playerId`：服务端分配给选手的公开数字编号，可安全用于公共比分和展示。
- `price`：整数摩拉价格。
- `quantity`：整数黄金数量。
- `currentMonth`：当前月份，范围 `1..3`。
- `currentDay`：当前月内交易日，范围通常为 `1..30`；策略阶段为 `0`。
- `currentTick`：整场比赛全局 tick。
- `totalTicks`：当前实现固定填 `30`，表示一个月的交易天数。
- `tick`：`MARKET_STATE.tick`，等于当前月内交易日。
- `arrivalTick`：订单实际到达撮合引擎的月内交易日。
- `publishTick`：新闻对应的实际发布日。内幕预览提前收到时，这个字段仍然指向未来的正式发布日。
- `prediction`：`Long`、`Short`、`Hold`。
- `side`：`Buy` 或 `Sell`。
- `intent`：`Immediate` 或 `Resting`。订单尚未到达时该字段可能为空字符串。
- `stage`：`Waiting`、`PreparingGame`、`StrategySelection`、`TradingDay`、`Settlement`、`Finished`。

## Client -> Server

所有当前支持的 client 消息都必须带 `token`。

### LIMIT_BUY

提交限价买单。

```json
{
  "messageType": "LIMIT_BUY",
  "token": "player1",
  "price": 2000,
  "quantity": 10
}
```

### LIMIT_SELL

提交限价卖单。

```json
{
  "messageType": "LIMIT_SELL",
  "token": "player1",
  "price": 2010,
  "quantity": 10
}
```

### CANCEL_ORDER

撤销订单。

```json
{
  "messageType": "CANCEL_ORDER",
  "token": "player1",
  "orderId": 123
}
```

### SUBMIT_REPORT

提交研报。

```json
{
  "messageType": "SUBMIT_REPORT",
  "token": "player1",
  "newsId": 5,
  "prediction": "Long"
}
```

### SELECT_STRATEGY

在策略阶段选择卡牌。

```json
{
  "messageType": "SELECT_STRATEGY",
  "token": "player1",
  "cardName": "内幕消息"
}
```

### ACTIVATE_SKILL

激活主动技能。

基础格式：

```json
{
  "messageType": "ACTIVATE_SKILL",
  "token": "player1",
  "skillName": "闪电交易"
}
```

字段：

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `skillName` | string | 是 | 技能名 |
| `targetPlayerId` | int | 否 | 当前用于 `网络风暴` 指定目标 |
| `variant` | string | 否 | 当前用于 `内幕消息`，支持 `"cheap"` |

已实现的主动技能参数约定：

- `内幕消息`：可带 `variant: "cheap"` 表示使用低价版本；省略时表示高价保真版本。
- `闪电交易`：无额外参数。
- `止损名刀`：无额外参数。
- `定向增发`：无额外参数。
- `网络风暴`：需要 `targetPlayerId`。
- `舆情打击`：无额外参数。

示例 1：网络风暴

```json
{
  "messageType": "ACTIVATE_SKILL",
  "token": "player1",
  "skillName": "网络风暴",
  "targetPlayerId": 1
}
```

示例 2：内幕消息低价版

```json
{
  "messageType": "ACTIVATE_SKILL",
  "token": "player1",
  "skillName": "内幕消息",
  "variant": "cheap"
}
```

## Server -> Client

### GAME_STATE

全局比赛状态。

```json
{
  "messageType": "GAME_STATE",
  "stage": "TradingDay",
  "currentMonth": 2,
  "currentDay": 11,
  "currentTick": 345,
  "totalTicks": 30,
  "scores": [
    { "playerId": 0, "score": 1 },
    { "playerId": 1, "score": 0 }
  ]
}
```

字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `stage` | string | 当前比赛阶段 |
| `currentMonth` | number | 当前月份 |
| `currentDay` | number | 当前月内交易日 |
| `currentTick` | number | 全局 tick |
| `totalTicks` | number | 当前实现固定为 `30` |
| `scores` | array | 当前比分，按 `playerId` 返回 |

### MARKET_STATE

市场快照。

```json
{
  "messageType": "MARKET_STATE",
  "bids": [{ "price": 1998, "quantity": 30 }],
  "asks": [{ "price": 2002, "quantity": 25 }],
  "lastPrice": 2000,
  "midPrice": 2000,
  "volume": 152,
  "tick": 11
}
```

字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `bids` | array | 买盘档位 |
| `asks` | array | 卖盘档位 |
| `lastPrice` | number | 最近成交价 |
| `midPrice` | number | 盘口中间价 |
| `volume` | number | 当前月份累计成交量 |
| `tick` | number | 当前月内交易日 |

### PLAYER_STATE

私有玩家状态，只发给对应 player。

```json
{
  "messageType": "PLAYER_STATE",
  "playerId": 0,
  "mora": 980000,
  "frozenMora": 20000,
  "gold": 1005,
  "frozenGold": 10,
  "lockedGold": 100,
  "nav": 1990000,
  "networkDelay": 1,
  "immediateOrdersUsedToday": 1,
  "restingOrdersUsedToday": 0,
  "bonusImmediateOrdersToday": 1,
  "monthlyTradeCount": 17,
  "activeCards": ["内幕消息"],
  "pendingOrders": [
    {
      "orderId": 123,
      "arrivalTick": 12,
      "side": "Buy",
      "price": 1998,
      "quantity": 10,
      "remainingQuantity": 10,
      "status": "Pending",
      "intent": ""
    }
  ]
}
```

说明：

- `pendingOrders` 同时包含“尚未到达的延迟订单”和“已进入订单簿但未完全成交的订单”。
- 订单尚未到达时，`intent` 可能为空；到达并处理后会变成 `Immediate` 或 `Resting`。
- `monthlyTradeCount` 用于当月结算平局时的交易数比较。

### STRATEGY_OPTIONS

策略候选。

```json
{
  "messageType": "STRATEGY_OPTIONS",
  "infrastructure": {
    "name": "内幕消息",
    "description": "花费摩拉提前3天获取下一条快讯，或以低价赌一半概率的伪消息",
    "category": "Infrastructure"
  },
  "riskControl": {
    "name": "止损名刀",
    "description": "撤销全部挂单，并在接下来3天将盘面下跌造成的净值亏损降为20%",
    "category": "RiskControl"
  },
  "finTech": {
    "name": "网络风暴",
    "description": "全局最多使用3次，使目标的下一次下单额外滞后1天",
    "category": "FinTech"
  }
}
```

说明：

- 每个月都会收到一组新的三分类候选。
- 由于每类只有 2 张卡，候选卡名在不同月份之间可以重复出现。
- 玩家自己仍不能重复选择同名卡。

### NEWS_BROADCAST

新闻消息。

```json
{
  "messageType": "NEWS_BROADCAST",
  "month": 2,
  "day": 11,
  "newsId": 5,
  "content": "璃月矿区运输恢复，黄金供给预期改善。",
  "publishTick": 11
}
```

说明：

- 同一 schema 同时用于公开新闻、伪造新闻、被污染的广播、以及内幕预览。
- 当前没有 `isFake`、`sourcePlayer`、`isPreview` 之类的区分字段。
- 对内幕预览而言，消息可能在第 `day - 3` 天提前送达，但 `day` 和 `publishTick` 仍表示未来正式新闻的发布日。

### REPORT_RESULT

研报结算结果，仅发送给提交该研报的 player。

```json
{
  "messageType": "REPORT_RESULT",
  "newsId": 5,
  "submissionRank": 1,
  "submitTick": 11,
  "settlementTick": 14,
  "prediction": "Long",
  "isCorrect": true,
  "reward": 12000,
  "actualChange": 8
}
```

字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `newsId` | number | 对应新闻 ID |
| `submissionRank` | number | 该新闻下的提交先后排名 |
| `submitTick` | number | 提交日 |
| `settlementTick` | number | 结算日 |
| `prediction` | string | `Long` / `Short` / `Hold` |
| `isCorrect` | bool | 是否预测正确 |
| `reward` | number | 奖励或罚金，负数表示罚金 |
| `actualChange` | number | 结算时的真实价格变化 |

### TRADE_NOTIFICATION

私有成交通知。买方和卖方各收到一条，`side` 以接收者视角表示。

```json
{
  "messageType": "TRADE_NOTIFICATION",
  "tradeId": 1001,
  "orderId": 123,
  "price": 2000,
  "quantity": 5,
  "side": "Buy",
  "fee": 2
}
```

### SKILL_EFFECT

技能效果广播。

```json
{
  "messageType": "SKILL_EFFECT",
  "skillName": "网络风暴",
  "sourcePlayerId": 0,
  "targetPlayerId": 1,
  "description": "next order delayed by 1 day"
}
```

字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `skillName` | string | 技能名 |
| `sourcePlayerId` | int | 触发者 PlayerID |
| `targetPlayerId` | int/null | 目标 PlayerID，没有目标时省略 |
| `description` | string | 服务端生成的效果描述 |

### DAY_SETTLEMENT

历史名称保留为 `DAY_SETTLEMENT`，但当前实际用于“月结算”。

```json
{
  "messageType": "DAY_SETTLEMENT",
  "day": 2,
  "month": 2,
  "winnerPlayerId": 0,
  "reason": "higher NAV",
  "cumulativeNavs": {
    "0": 4050000,
    "1": 3980000
  },
  "finalBonusWinnerPlayerId": -1,
  "finalBonusPoints": 0,
  "players": [
    {
      "playerId": 0,
      "nav": 2034500,
      "mora": 1034500,
      "gold": 1000,
      "frozenMora": 0,
      "frozenGold": 0,
      "lockedGold": 0,
      "tradeCount": 17,
      "activeCards": ["内幕消息"]
    }
  ]
}
```

说明：

- 该消息在每个月结算时发送一次。
- `month` 是权威字段。
- 当前服务端会把 `day` 也填成同样的月份编号；客户端如果要展示月份，请使用 `month`。
- `finalBonusWinnerPlayerId` 和 `finalBonusPoints` 只会在第 3 个月结算时有意义（胜者 PlayerID，无胜者时为 -1）。

### ERROR

错误消息 schema：

```json
{
  "messageType": "ERROR",
  "errorCode": 3002,
  "message": "Action is not allowed in the current stage."
}
```

当前说明：

- `ErrorMessage` 类型已定义。
- 当前服务端普通动作失败多数不会主动返回该消息，而是直接拒绝或忽略。
- 前端和 SDK 不应假设所有失败都会收到 `ERROR`。

## 阶段状态机

当前比赛阶段：

```text
Waiting -> PreparingGame -> StrategySelection -> TradingDay -> Settlement
         -> StrategySelection -> TradingDay -> Settlement
         -> StrategySelection -> TradingDay -> Settlement -> Finished
```

阶段说明：

- `Waiting`：等待开局。
- `PreparingGame`：从等待阶段切到新月份策略阶段的过渡。
- `StrategySelection`：当前月份选卡阶段。
- `TradingDay`：当前月份 30 天交易期。
- `Settlement`：当前月份结算。
- `Finished`：整局结束。

## 广播时机

- 每个 game tick：发送 `GAME_STATE`。
- `TradingDay` / `Settlement` 阶段的每个 tick：发送 `MARKET_STATE` 和 `PLAYER_STATE`。
- `StrategySelection` 阶段的每个 tick：发送 `STRATEGY_OPTIONS`。
- 新闻、技能、成交、研报结算等事件，会在下一次广播周期里附带发送。
