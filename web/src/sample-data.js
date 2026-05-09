export function buildSampleMessages(role = "observer") {
  const helloRole = role === "admin" ? "admin" : role === "player" ? "player" : "observer";
  const messages = [
    {
      messageType: "HELLO_ACK",
      role: helloRole,
      protocolVersion: "v1",
      capabilities: ["gameState", "marketState", "playerSummary", "events"],
    },
    {
      messageType: "GAME_STATE",
      stage: "TradingDay",
      currentDay: 1,
      currentTick: 260,
      totalTicks: 1,
      scores: [
        { token: "player1", score: 0 },
        { token: "player2", score: 0 },
      ],
    },
    {
      messageType: "STRATEGY_OPTIONS",
      infrastructure: {
        name: "内幕消息",
        description: "提前 3 Tick 收到新闻预览。",
        category: "Infrastructure",
      },
      riskControl: {
        name: "冰山订单",
        description: "公开盘口只显示部分挂单数量。",
        category: "RiskControl",
      },
      finTech: {
        name: "暗池交易",
        description: "按中间价与系统成交固定数量。",
        category: "FinTech",
      },
    },
  ];

  let volume = 0;
  for (let tick = 1; tick <= 150; tick += 1) {
    const drift = tick * 0.42;
    const wave = Math.sin(tick / 8) * 18 + Math.cos(tick / 17) * 9;
    const midPrice = Math.round(1000 + drift + wave);
    const spread = 4 + (tick % 4);
    volume += tick % 9 === 0 ? 7 : tick % 5 === 0 ? 4 : 1;

    messages.push({
      messageType: "GAME_STATE",
      stage: "TradingDay",
      currentDay: 1,
      currentTick: 260 + tick,
      totalTicks: tick,
      scores: [
        { token: "player1", score: 0 },
        { token: "player2", score: 0 },
      ],
    });

    messages.push({
      messageType: "MARKET_STATE",
      bids: makeLevels(midPrice - spread, -1, 10),
      asks: makeLevels(midPrice + spread, 1, 10),
      lastPrice: midPrice + ((tick % 3) - 1),
      midPrice,
      volume,
      tick,
    });

    if (tick % 25 === 0) {
      messages.push(summary("player1", 1000000 + tick * 180, 1000 - Math.floor(tick / 20), midPrice, 1));
      messages.push(summary("player2", 998000 + tick * 120, 1000 + Math.floor(tick / 24), midPrice, 2));
    }

    if (tick === 18) {
      messages.push({
        messageType: "PLAYER_STATE",
        mora: 982400,
        frozenMora: 18000,
        gold: 1004,
        frozenGold: 8,
        lockedGold: 0,
        nav: 1998800,
        activeCards: ["内幕消息", "暗池交易"],
        pendingOrders: [
          {
            orderId: 101,
            side: "Buy",
            price: midPrice - 6,
            quantity: 20,
            remainingQuantity: 12,
            status: "Pending",
          },
        ],
      });
    }

    if (tick === 32) {
      messages.push({
        messageType: "NEWS_BROADCAST",
        newsId: 7,
        content: "层岩巨渊运输线临时管制，黄金现货供给预期收紧。",
        publishTick: tick,
      });
    }

    if (tick === 72) {
      messages.push({
        messageType: "NEWS_BROADCAST",
        newsId: 8,
        content: "飞云商会称黄金运输恢复，部分现货报价松动。",
        publishTick: tick,
        isFake: true,
        sourcePlayer: "player2",
      });
    }

    if (tick === 54) {
      messages.push({
        messageType: "TRADE_NOTIFICATION",
        tradeId: 9001,
        orderId: 101,
        price: midPrice,
        quantity: 12,
        side: "Buy",
        fee: 2,
        tick,
      });
    }

    if (tick === 78) {
      messages.push({
        messageType: "SKILL_EFFECT",
        skillName: "拔网线",
        sourcePlayer: "player2",
        description: "交易所进入熔断状态 20 Tick。",
      });
    }

    if (tick === 118) {
      messages.push({
        messageType: "REPORT_RESULT",
        newsId: 7,
        prediction: "Long",
        isCorrect: true,
        reward: 14800,
        actualChange: 11,
      });
    }

    if (tick === 132) {
      messages.push({
        messageType: "REPORT_RESULT",
        newsId: 8,
        prediction: "Short",
        isCorrect: false,
        reward: -9200,
        actualChange: 7,
      });
    }
  }

  messages.push({
    messageType: "DAY_SETTLEMENT",
    day: 1,
    winnerToken: "player1",
    reason: "NAV",
    scores: [
      { token: "player1", score: 1 },
      { token: "player2", score: 0 },
    ],
    players: [
      { token: "player1", nav: 2034500, tradeCount: 17 },
      { token: "player2", nav: 1992000, tradeCount: 13 },
    ],
  });

  return messages;
}

function makeLevels(basePrice, step, count) {
  const levels = [];
  for (let index = 0; index < count; index += 1) {
    levels.push({
      price: basePrice + index * step * 2,
      quantity: 12 + ((index * 7) % 35),
    });
  }
  return levels;
}

function summary(token, mora, gold, midPrice, pendingOrderCount) {
  return {
    messageType: "PLAYER_SUMMARY_STATE",
    token,
    mora,
    frozenMora: pendingOrderCount * 9000,
    gold,
    frozenGold: pendingOrderCount * 3,
    lockedGold: token === "player2" ? 100 : 0,
    nav: mora + gold * midPrice,
    activeCards: token === "player1" ? ["内幕消息", "暗池交易"] : ["冰山订单", "拔网线"],
    pendingOrderCount,
    tradeCount: 8 + pendingOrderCount,
  };
}
