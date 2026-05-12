namespace DrawSync.Helpers
{
    public class PlanLimits
    {
        public static readonly Dictionary<string, (int drawings, int members)> Limits = new()
        {
            { "free", (drawings: 1, members: 3) },
            { "pro", (drawings: 5, members: 15) }
        };

        public static (int drawings, int members) GetLimits(string plan)
        {
            return Limits.TryGetValue(plan.ToLower(), out var limit) ? limit : Limits["free"];
        }

        public static bool CanCreateDrawing(string plan, int currentDrawingsCount)
        {
            var (maxDrawings, _) = GetLimits(plan);
            return currentDrawingsCount < maxDrawings;
        }

        public static bool CanAddMember(string plan, int currentMembersCount)
        {
            var (_, maxMembers) = GetLimits(plan);
            return currentMembersCount < maxMembers;
        }
    }
}
