namespace Thuai.GameLogic;

public class ResearchSystem
{
    private readonly NewsSystem _newsSystem;
    private readonly long _baseReward;
    private readonly int _researchWindow;
    private readonly int _settlementDelay;
    private readonly List<ResearchReport> _pendingReports = new();
    private readonly List<ResearchReport> _settledReports = new();

    public ResearchSystem(NewsSystem newsSystem, long baseReward = 10000,
                          int researchWindow = 2, int settlementDelay = 3)
    {
        _newsSystem = newsSystem;
        _baseReward = baseReward;
        _researchWindow = researchWindow;
        _settlementDelay = settlementDelay;
    }

    public ResearchReport? SubmitReport(string playerToken, int newsId, Prediction prediction,
        int currentTick, int? playerResearchWindow = null, double decayMultiplier = 1.0)
    {
        var news = _newsSystem.GetNews(newsId);
        if (news == null) return null;

        int effectiveWindow = playerResearchWindow ?? _researchWindow;
        int ticksUsed = currentTick - news.PublishTick;
        if (ticksUsed < 0 || ticksUsed > effectiveWindow) return null;

        if (_pendingReports.Any(r => r.PlayerToken == playerToken && r.NewsId == newsId))
            return null;
        if (_settledReports.Any(r => r.PlayerToken == playerToken && r.NewsId == newsId))
            return null;

        var report = new ResearchReport
        {
            PlayerToken = playerToken,
            NewsId = newsId,
            Prediction = prediction,
            SubmitTick = currentTick,
            SubmitDay = currentTick,
            SettlementDay = news.PublishTick + _settlementDelay
        };

        _pendingReports.Add(report);
        return report;
    }

    public List<ResearchReport> SettleReports(int currentTick, Func<int, long> getMidPriceAtTick)
    {
        var settled = new List<ResearchReport>();
        var dueReports = _pendingReports
            .Where(report =>
            {
                var news = _newsSystem.GetNews(report.NewsId);
                return news != null && currentTick >= news.PublishTick + _settlementDelay;
            })
            .GroupBy(report => report.NewsId)
            .ToList();

        foreach (var group in dueReports)
        {
            var news = _newsSystem.GetNews(group.Key);
            if (news == null)
                continue;

            int settlementTick = news.PublishTick + _settlementDelay;
            long priceAtPublish = getMidPriceAtTick(news.PublishTick);
            long priceAtSettlement = getMidPriceAtTick(settlementTick);
            long actualChange = priceAtSettlement - priceAtPublish;
            long magnitude = Math.Abs(actualChange);

            var ordered = group
                .OrderBy(report => report.SubmitTick)
                .ThenBy(report => report.PlayerToken)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var report = ordered[i];
                report.ActualChange = actualChange;
                report.SubmissionRank = i + 1;

                bool isCorrect = report.Prediction switch
                {
                    Prediction.Long => actualChange > 0,
                    Prediction.Short => actualChange < 0,
                    _ => actualChange == 0
                };

                if (news.IsFake && report.PlayerToken != news.SourcePlayer)
                    isCorrect = false;

                report.IsCorrect = isCorrect;
                if (magnitude == 0)
                {
                    report.Reward = 0;
                }
                else
                {
                    long rankMultiplier = Math.Max(1, ordered.Count - i);
                    long rewardMagnitude = _baseReward * rankMultiplier * magnitude;
                    report.Reward = isCorrect ? rewardMagnitude : -rewardMagnitude;
                }

                _pendingReports.Remove(report);
                _settledReports.Add(report);
                settled.Add(report);
            }
        }

        return settled;
    }

    public List<ResearchReport> GetPendingReports(string playerToken)
    {
        return _pendingReports.Where(r => r.PlayerToken == playerToken).ToList();
    }

    public IReadOnlyList<ResearchReport> PendingReports => _pendingReports;

    public IReadOnlyList<ResearchReport> SettledReports => _settledReports;

    public void Reset()
    {
        _pendingReports.Clear();
        _settledReports.Clear();
    }
}
