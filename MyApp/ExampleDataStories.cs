using MyApp.ServiceInterface;
using MyApp.ServiceModel;

namespace MyApp;

/// <summary>
/// The example organizations and what happened to each of them over the last year: when they subscribed, changed
/// plan or left, who joined them and how much they used. It's generated from fixed seeds, so every run of the
/// seeder tells the same story, relative to the day it's run.
/// </summary>
internal static class ExampleDataStories
{
    /// <summary>
    /// The history that's created, which is as much as SaasConfig.AnalyticsRetentionDays keeps by default
    /// </summary>
    public const int HistoryDays = 365;

    internal const int TrialDays = 14;

    public static List<OrganizationStory> Create(DemoUser manager, DemoUser employee, DemoUser test)
    {
        // The screenshot organizations, whose current state the docs describe
        List<OrganizationStory> stories = [
            new("demo.workspace.northstar", "Northstar Labs", "northstar-labs", "finance@northstar.example") {
                Plans = [new(74, "business", SubscriptionStatus.Trialing), new(60, "business", SubscriptionStatus.Active)],
                Members = [
                    new(manager.Id, manager.Email, WorkspaceMemberRole.Owner, 74, 1.0),
                    new(employee.Id, employee.Email, WorkspaceMemberRole.Admin, 53, 0.7),
                    new(test.Id, test.Email, WorkspaceMemberRole.Member, 42, 0.4),
                ],
                Trend = UsageTrend.Growing, Intensity = 0.6, CurrentApiUse = 0.54, CurrentDocumentsUploaded = 138,
                HasHandWrittenHistory = true,
            },
            new("demo.workspace.harbor", "Harbor & Pine", "harbor-and-pine", "accounts@harborpine.example") {
                Plans = [new(26, "pro", SubscriptionStatus.Trialing)], TrialEndsInDays = 8,
                Members = Team("demo.user.harbor", "maya", "harborpine.example", 26, 3),
                Trend = UsageTrend.Rocket, Intensity = 0.8, CurrentApiUse = 0.61,
                SupportNote = "Trial is extended to finish a pilot with their two largest clients. Expansion to Business likely if it converts.",
            },
            new("demo.workspace.meridian", "Meridian Health", "meridian-health", "billing@meridian.example") {
                Plans = [new(118, "enterprise", SubscriptionStatus.Active)],
                Members = Team("demo.user.meridian", "samira", "meridian.example", 118, 12),
                Trend = UsageTrend.Steady, Intensity = 0.55,
                SupportNote = "Enterprise contract is invoiced outside the plan catalog. Quarterly business review is due next month.",
            },
            new("demo.workspace.redwood", "Redwood Studio", "redwood-studio", "hello@redwood.example") {
                Plans = [new(43, "pro", SubscriptionStatus.Trialing), new(29, "pro", SubscriptionStatus.Active), new(6, "pro", SubscriptionStatus.PastDue)],
                Members = Team("demo.user.redwood", "eli", "redwood.example", 43, 4),
                Trend = UsageTrend.Spiky, Intensity = 0.85, CurrentApiUse = 0.87,
                SupportNote = "Card was declined twice. They're close to their API limit, so an upgrade conversation may also recover the payment.",
            },
            new("demo.workspace.solo", "Avery Chen", "avery-chen", "avery@example.com", WorkspaceKind.Individual) {
                Plans = [new(19, "personal", SubscriptionStatus.Trialing), new(5, "personal", SubscriptionStatus.Active)],
                Members = [new("demo.user.avery", "avery@example.com", WorkspaceMemberRole.Owner, 19, 1.0)],
                Trend = UsageTrend.Steady, Intensity = 0.35,
            },
            new("demo.workspace.atlas", "Atlas Legal", "atlas-legal", "operations@atlaslegal.example") {
                Plans = [new(91, "business", SubscriptionStatus.Trialing), new(77, "business", SubscriptionStatus.Active)],
                Members = Team("demo.user.atlas", "nora", "atlaslegal.example", 91, 9),
                Trend = UsageTrend.Steady, Intensity = 0.45,
            },
        ];

        // A year of customers, each with one of these stories
        (string Name, Arc Arc, string Plan, string? ToPlan, string? Note)[] customers = [
            ("Cobalt Ridge", Arc.Steady, "business", null, null),
            ("Kestrel Systems", Arc.Steady, "business", null, null),
            ("Granite Peak Partners", Arc.Steady, "business", null, null),
            ("Polaris Insurance", Arc.Steady, "business", null, null),
            ("Juniper & Co", Arc.Steady, "pro", null, null),
            ("Saffron Studio", Arc.Steady, "pro", null, null),
            ("Willow Health", Arc.Steady, "pro", null, null),
            ("Quill & Ledger", Arc.Steady, "pro", null, null),
            ("Aurora Learning", Arc.Steady, "pro", null, null),
            ("Lighthouse Clinic", Arc.Steady, "pro", null, null),
            ("Keystone Accounting", Arc.Steady, "pro", null, null),
            ("Orbital Freight", Arc.Growing, "pro", null, "Usage has doubled since they connected their warehouse system. A Business upgrade should come up at renewal."),
            ("Brightwater Labs", Arc.Growing, "pro", null, null),
            ("Sable Security", Arc.Growing, "pro", null, null),
            ("Lumen Analytics", Arc.Upgrade, "pro", "business", null),
            ("Bluefin Logistics", Arc.Upgrade, "pro", "business", null),
            ("Starling Apps", Arc.Upgrade, "pro", "business", "Upgraded after hitting the Pro API limit three months running."),
            ("Copperline Energy", Arc.Upgrade, "pro", "business", null),
            ("Pioneer Robotics", Arc.FreeToPaid, "free", "pro", null),
            ("Silverleaf Wealth", Arc.FreeToPaid, "free", "pro", null),
            ("Beacon Hill Law", Arc.FreeToPaid, "free", "pro", null),
            ("Tidewater Capital", Arc.Downgrade, "business", "pro", "Downgraded after a budget review. They still use Business features, so a win-back offer could work."),
            ("Vantage Point Media", Arc.Downgrade, "business", "pro", null),
            ("Ferrous Robotics", Arc.Churned, "business", null, "Canceled after being acquired. The parent company standardised on another vendor."),
            ("Halcyon Travel", Arc.Churned, "pro", null, "Usage fell steadily for two months before canceling; their main contact left the company."),
            ("Nimbus Retail", Arc.Churned, "pro", null, null),
            ("Driftwood Hotels", Arc.Churned, "pro", null, null),
            ("Fieldstone Farms", Arc.Declining, "pro", null, "Usage is down sharply this quarter. Nobody has logged in from their data team recently."),
            ("Riverbend Dental", Arc.Declining, "pro", null, null),
            ("Ironbark Builders", Arc.PastDue, "business", null, "Invoice failed after their company card expired. Their finance contact is on leave until next week."),
            ("Crescent Bioworks", Arc.Trial, "pro", null, null),
            ("Foxglove Events", Arc.Trial, "pro", null, null),
            ("Larkspur Ventures", Arc.Trial, "business", null, "Evaluating Business for a team of twelve. Asked about SSO and audit history."),
            ("Summit Outdoor", Arc.TrialExpired, "pro", null, null),
            ("Evergreen Schools", Arc.Free, "free", null, null),
            ("Mosaic Design Co", Arc.Free, "free", null, null),
            ("Priya Raman", Arc.Steady, "personal", null, null),
            ("Marcus Webb", Arc.Steady, "personal", null, null),
            ("Sofia Alvarez", Arc.Growing, "personal", null, null),
            ("Kenji Watanabe", Arc.FreeToPaid, "free", "personal", null),
            ("Lena Fischer", Arc.Free, "free", null, null),
            ("Omar Haddad", Arc.Free, "free", null, null),
            ("Grace Okafor", Arc.Free, "free", null, null),
            ("Tomás Silva", Arc.Churned, "personal", null, null),
        ];
        // Customers who've stopped using it
        var inactive = new Dictionary<string, int> { ["Fieldstone Farms"] = 16 };
        // Customers who've filled their plan's seats
        var fullTeams = new Dictionary<string, int> { ["Orbital Freight"] = 5 };
        string[] individuals = ["Priya Raman", "Marcus Webb", "Sofia Alvarez", "Kenji Watanabe", "Lena Fischer", "Omar Haddad", "Grace Okafor", "Tomás Silva"];
        foreach (var customer in customers)
        {
            var individual = individuals.Contains(customer.Name);
            stories.Add(Generate(customer.Name, individual ? WorkspaceKind.Individual : WorkspaceKind.Business,
                customer.Arc, customer.Plan, customer.ToPlan, customer.Note, inactive.GetValueOrDefault(customer.Name),
                fullTeams.TryGetValue(customer.Name, out var size) ? size : null));
        }
        return stories;
    }

    private enum Arc { Steady, Growing, Upgrade, FreeToPaid, Downgrade, Churned, Declining, PastDue, Trial, TrialExpired, Free }

    private static OrganizationStory Generate(string name, WorkspaceKind kind, Arc arc, string plan, string? toPlan, string? note,
        int inactiveDays, int? teamSize)
    {
        var slug = Slugify(name);
        var random = new Random(StableSeed(slug));
        var domain = kind == WorkspaceKind.Individual ? "example.com" : $"{slug}.example";
        List<PlanSegment> plans = [];
        // Established customers predate the history, so it starts with recurring revenue
        var created = arc switch {
            Arc.Trial => random.Next(2, 12),
            Arc.TrialExpired => random.Next(40, 120),
            Arc.PastDue => random.Next(120, 500),
            Arc.Free => random.Next(30, HistoryDays),
            Arc.Steady or Arc.Growing or Arc.Declining => random.Next(120, 2 * HistoryDays),
            _ => random.Next(220, 2 * HistoryDays),
        };
        // What changed happens within the history
        var changed = Math.Min(created, HistoryDays - 30);
        void Subscribe(int daysAgo, string code)
        {
            plans.Add(new(daysAgo, code, SubscriptionStatus.Trialing));
            if (daysAgo - TrialDays > 0) plans.Add(new(daysAgo - TrialDays, code, SubscriptionStatus.Active));
        }
        var trend = UsageTrend.Steady;
        switch (arc)
        {
            case Arc.Free:
                plans.Add(new(created, "free", SubscriptionStatus.Free));
                break;
            case Arc.Trial:
                Subscribe(created, plan);
                break;
            case Arc.TrialExpired:
                Subscribe(created, plan);
                plans[^1] = new(created - TrialDays, "free", SubscriptionStatus.Free);
                break;
            case Arc.FreeToPaid:
                plans.Add(new(created, "free", SubscriptionStatus.Free));
                Subscribe(random.Next(TrialDays + 6, changed - 30), toPlan!);
                trend = UsageTrend.Growing;
                break;
            case Arc.Upgrade:
                Subscribe(created, plan);
                plans.Add(new(random.Next(25, changed - 60), toPlan!, SubscriptionStatus.Active));
                trend = UsageTrend.Growing;
                break;
            case Arc.Downgrade:
                Subscribe(created, plan);
                plans.Add(new(random.Next(20, 90), toPlan!, SubscriptionStatus.Active));
                break;
            case Arc.Churned:
                Subscribe(created, plan);
                plans.Add(new(random.Next(20, changed - 60), plan, SubscriptionStatus.Canceled));
                trend = UsageTrend.Declining;
                break;
            case Arc.Declining:
                Subscribe(created, plan);
                trend = UsageTrend.Declining;
                break;
            case Arc.PastDue:
                Subscribe(created, plan);
                plans.Add(new(random.Next(3, 10), plan, SubscriptionStatus.PastDue));
                break;
            default:
                Subscribe(created, plan);
                trend = arc == Arc.Growing ? UsageTrend.Growing : random.Next(4) == 0 ? UsageTrend.Spiky : UsageTrend.Steady;
                break;
        }
        // Most teams use less than 70% of the most seats they've had, e.g. 2 or 3 of Pro's 5
        var seats = plans.Max(x => x.PlanCode switch { "business" => 12, "pro" => 4, _ => 1 });
        var owner = kind == WorkspaceKind.Individual ? slug.Split('-')[0] : FirstNames[random.Next(FirstNames.Length)];
        return new($"demo.workspace.{slug}", name, slug, kind == WorkspaceKind.Individual ? $"{owner}@{domain}" : $"billing@{domain}", kind) {
            Plans = plans,
            Members = Team($"demo.user.{slug}", owner, domain, created, teamSize ?? (seats == 1 ? 1 : random.Next(2, seats)), random),
            Trend = trend, Intensity = 0.3 + random.NextDouble() * 0.45, SupportNote = note, InactiveDays = inactiveDays,
        };
    }

    private static readonly string[] FirstNames = [
        "alex", "jamie", "morgan", "taylor", "riley", "casey", "jordan", "sam", "drew", "quinn",
        "reese", "harper", "rowan", "emery", "parker", "logan", "cameron", "dakota", "kai", "elliot",
    ];

    /// <summary>
    /// The owner, who joins when the organization is created, and the members who join over its life
    /// </summary>
    private static List<StoryMember> Team(string userIdPrefix, string owner, string domain, int createdDaysAgo, int size, Random? random = null)
    {
        random ??= new Random(StableSeed(userIdPrefix));
        List<StoryMember> members = [new(userIdPrefix, $"{owner}@{domain}", WorkspaceMemberRole.Owner, createdDaysAgo, 1.0)];
        var names = FirstNames.Where(x => x != owner).OrderBy(_ => random.Next()).Take(size - 1).ToList();
        for (var i = 0; i < names.Count; i++)
        {
            var role = i == 0 && size > 3 ? WorkspaceMemberRole.Admin : i == 1 && size > 6 ? WorkspaceMemberRole.Billing : WorkspaceMemberRole.Member;
            members.Add(new($"{userIdPrefix}.{names[i]}", $"{names[i]}@{domain}", role,
                random.Next(0, Math.Max(1, createdDaysAgo - 3)), 0.2 + random.NextDouble() * 0.7));
        }
        return members;
    }

    private static string Slugify(string name) => string.Concat(name.ToLowerInvariant()
            .Replace("&", "and").Replace("á", "a")
            .Select(c => char.IsLetterOrDigit(c) ? c : '-'))
        .Replace("--", "-").Trim('-');

    // string.GetHashCode() differs between runs, so the seeds are FNV-1a hashes
    internal static int StableSeed(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value) hash = (hash ^ c) * 16777619u;
        return (int)(hash & 0x7FFFFFFF);
    }
}

internal sealed record DemoUser(string Id, string Email);

internal enum UsageTrend { Steady, Growing, Rocket, Declining, Spiky }

/// <summary>
/// From this many days ago the organization is on this plan, with this status
/// </summary>
internal sealed record PlanSegment(int DaysAgo, string PlanCode, SubscriptionStatus Status);

internal sealed record StoryMember(string UserId, string Email, WorkspaceMemberRole Role, int JoinedDaysAgo, double Weight);

internal sealed class OrganizationStory(string id, string name, string slug, string billingEmail, WorkspaceKind kind = WorkspaceKind.Business)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Slug { get; } = slug;
    public string BillingEmail { get; } = billingEmail;
    public WorkspaceKind Kind { get; } = kind;

    /// <summary>Its subscription's history, oldest first. It's created when its first segment starts.</summary>
    public required List<PlanSegment> Plans { get; init; }
    /// <summary>The owner first, then the members who joined it</summary>
    public required List<StoryMember> Members { get; init; }
    public UsageTrend Trend { get; init; }
    /// <summary>The share of its plan's API allowance it uses at its busiest</summary>
    public double Intensity { get; init; }
    /// <summary>The share of this billing period's API allowance that's used, for the organizations the docs describe</summary>
    public double? CurrentApiUse { get; init; }
    public long? CurrentDocumentsUploaded { get; init; }
    /// <summary>When a current trial ends, otherwise it ends 14 days after it starts</summary>
    public int? TrialEndsInDays { get; init; }
    /// <summary>The days since it last used anything</summary>
    public int InactiveDays { get; init; }
    /// <summary>What the platform's operators know about the customer</summary>
    public string? SupportNote { get; init; }
    /// <summary>Its lifecycle events are written by hand for the screenshots</summary>
    public bool HasHandWrittenHistory { get; init; }

    public int CreatedDaysAgo => Plans[0].DaysAgo;
    public PlanSegment Current => Plans[^1];
    public int? CanceledDaysAgo => Current.Status == SubscriptionStatus.Canceled ? Current.DaysAgo : null;
    public string OwnerId => Members[0].UserId;

    /// <summary>The plan it was on that day, or null before it was created</summary>
    public PlanSegment? PlanOn(int daysAgo) => Plans.LastOrDefault(x => x.DaysAgo >= daysAgo);

    /// <summary>
    /// The billing period that contains the day. Free organizations use calendar months; a trial is its own period;
    /// a paid subscription's periods are monthly from when it was first billed, and keep that anchor when it changes
    /// plan. A canceled subscription keeps its last period.
    /// </summary>
    public (DateTime Start, DateTime End) BillingPeriodOn(int daysAgo, DateTime today)
    {
        var index = Plans.FindLastIndex(x => x.DaysAgo >= daysAgo);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(daysAgo), $"{Name} didn't exist {daysAgo} days ago.");
        if (Plans[index].Status == SubscriptionStatus.Canceled)
            return BillingPeriodOn(Plans[index].DaysAgo + 1, today);
        var date = today.AddDays(-daysAgo);
        var segment = Plans[index];
        if (segment.Status == SubscriptionStatus.Free)
        {
            var month = new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            return (month, month.AddMonths(1));
        }
        if (segment.Status == SubscriptionStatus.Trialing)
        {
            var start = today.AddDays(-segment.DaysAgo);
            var end = index + 1 < Plans.Count
                ? today.AddDays(-Plans[index + 1].DaysAgo)
                : today.AddDays(TrialEndsInDays ?? Math.Max(1, ExampleDataStories.TrialDays - segment.DaysAgo));
            return (start, end);
        }
        // Billed segments since it was last trialing or free
        var anchorIndex = index;
        while (anchorIndex > 0 && SaasRevenue.IsBilled(Plans[anchorIndex - 1].Status)) anchorIndex--;
        var anchor = today.AddDays(-Plans[anchorIndex].DaysAgo);
        var periodStart = anchor;
        while (periodStart.AddMonths(1) <= date) periodStart = periodStart.AddMonths(1);
        return (periodStart, periodStart.AddMonths(1));
    }
}

internal sealed record DailyUsage(int DaysAgo, long ApiRequests, long DocumentsUploaded);

internal static class ExampleUsage
{
    // The API requests that an uploaded document comes with
    private const double DocumentsPerRequest = 0.0035;
    // Usage of an allowance that isn't limited, e.g. a contact-sales plan, is shaped as if it were this
    private const long UnlimitedApiRequests = 3_000_000;

    /// <summary>
    /// Each day's usage, from when the organization was created until today or it canceled. A billing period's usage
    /// stays within its API allowance, which is enforced, and the current period's usage is what the docs describe.
    /// </summary>
    public static List<DailyUsage> Simulate(OrganizationStory story, DateTime today, Func<string, string, long?> allowance)
    {
        var random = new Random(ExampleDataStories.StableSeed($"{story.Slug}:usage"));
        var first = Math.Min(story.CreatedDaysAgo, ExampleDataStories.HistoryDays - 1);
        var ended = story.CanceledDaysAgo ?? 0;
        var lifetime = Math.Max(1, story.CreatedDaysAgo - ended);
        var api = new Dictionary<int, double>();
        for (var daysAgo = first; daysAgo >= 0; daysAgo--)
        {
            var plan = story.PlanOn(daysAgo)!;
            if (plan.Status == SubscriptionStatus.Canceled) break;
            var progress = (story.CreatedDaysAgo - daysAgo) / (double)lifetime;
            var trend = story.Trend switch {
                UsageTrend.Growing => 0.25 + 0.75 * progress,
                UsageTrend.Rocket => 0.08 + 0.92 * Math.Pow(progress, 1.6),
                // Customers who are losing interest use it less over the last few months
                UsageTrend.Declining => Math.Clamp(0.25 + 0.75 * (daysAgo - ended) / 75.0, 0.25, 1),
                UsageTrend.Spiky => 0.55 + (random.NextDouble() < 0.08 ? 1.3 : 0),
                _ => 0.85 + 0.15 * progress,
            };
            // Customers who leave wind down in the weeks before they cancel
            if (story.CanceledDaysAgo is { } canceled)
                trend *= Math.Clamp((daysAgo - canceled) / 45.0, 0.05, 1);
            if (daysAgo < story.InactiveDays) continue;
            var date = today.AddDays(-daysAgo);
            var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var weekday = weekend ? story.Kind == WorkspaceKind.Individual ? 0.8 : 0.35 : 1.0;
            var monthly = allowance(plan.PlanCode, "api.requests") ?? UnlimitedApiRequests;
            api[daysAgo] = monthly / 30.0 * story.Intensity * trend * weekday * (0.85 + 0.3 * random.NextDouble());
        }

        // Each billing period stays within its allowance, and the current one uses what the docs describe
        foreach (var period in api.Keys.GroupBy(x => story.BillingPeriodOn(x, today).Start))
        {
            var days = period.ToList();
            var plan = story.PlanOn(days.Min())!;
            var limit = allowance(plan.PlanCode, "api.requests");
            var total = days.Sum(x => api[x]);
            var target = days.Contains(0) && story.CurrentApiUse != null && limit != null
                ? story.CurrentApiUse.Value * limit.Value
                : limit != null && total > limit * 0.97 ? limit.Value * (0.9 + 0.07 * random.NextDouble()) : total;
            if (total > 0 && Math.Abs(target - total) > 0.5)
                foreach (var day in days) api[day] *= target / total;
        }

        var usage = api.OrderByDescending(x => x.Key).Select(x => {
            var requests = (long)Math.Round(x.Value);
            var documents = requests * DocumentsPerRequest;
            return new DailyUsage(x.Key, requests, (long)documents + (random.NextDouble() < documents % 1 ? 1 : 0));
        }).ToList();

        if (story.CurrentDocumentsUploaded is { } uploaded)
        {
            var start = story.BillingPeriodOn(0, today).Start;
            var current = usage.Where(x => today.AddDays(-x.DaysAgo) >= start).ToList();
            var total = Math.Max(1, current.Sum(x => x.DocumentsUploaded));
            long allocated = 0;
            for (var i = 0; i < current.Count; i++)
            {
                var documents = i == current.Count - 1 ? uploaded - allocated : current[i].DocumentsUploaded * uploaded / total;
                allocated += documents;
                usage[usage.IndexOf(current[i])] = current[i] with { DocumentsUploaded = documents };
            }
        }
        return usage;
    }
}
