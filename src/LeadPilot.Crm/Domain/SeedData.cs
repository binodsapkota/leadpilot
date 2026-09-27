namespace LeadPilot.Crm.Domain;

/// <summary>
/// Demo data for Fernway, a fictional workflow-automation vendor. Each CRM workspace starts from a fresh copy.
/// </summary>
public static class SeedData
{
    public static readonly DateOnly Today = new(2026, 9, 27);

    public static readonly Rep[] Reps =
    [
        new("R-JB", "Jordan Blake", "Account Executive", "jordan.blake@fernway.io", "North America mid-market (200-1,999 employees)"),
        new("R-AR", "Aisha Rahman", "Senior Account Executive", "aisha.rahman@fernway.io", "North America enterprise (2,000+ employees)"),
        new("R-LW", "Lukas Weber", "Account Executive", "lukas.weber@fernway.io", "EMEA"),
        new("R-NQ", "Nurture queue", "Marketing automation", "growth@fernway.io", "Leads not ready for sales"),
    ];

    public static readonly string[] CompetitorDomains = ["flowbolt.com", "opsloop.io"];

    public static readonly string[] PersonalEmailDomains = ["gmail.com", "yahoo.com", "outlook.com", "hotmail.com", "icloud.com", "proton.me"];

    public static readonly CompanyProfile[] Companies =
    [
        new("harborline.com", "Harborline Logistics", "logistics",
            "Third-party logistics and cross-border freight forwarding between the US and Mexico.",
            "Memphis, TN", "US", 1200, 410, ["NetSuite", "Salesforce", "Snowflake"]),
        new("quillstack.io", "Quillstack", "saas",
            "B2B SaaS for configure-price-quote (CPQ) and subscription billing.",
            "San Francisco, CA", "US", 650, 120, ["Salesforce", "NetSuite", "Stripe"]),
        new("tidewaterhealth.es", "Tidewater Health", "healthcare",
            "Private hospital and outpatient clinic network on the Spanish east coast.",
            "Valencia", "ES", 800, 190, ["SAP", "Microsoft Dynamics"]),
        new("brightmark.com", "Brightmark Retail", "retail",
            "Regional home-goods retail chain with 140 stores.",
            "Columbus, OH", "US", 2600, 880, ["SAP", "Salesforce"]),
        new("ardentfreight.com", "Ardent Freight", "logistics",
            "Road freight and contract logistics across Germany and Benelux.",
            "Hamburg", "DE", 450, 95, ["NetSuite", "Microsoft 365"]),
        new("kestrelfreight.com", "Kestrel Freight", "logistics",
            "Less-than-truckload carrier in the US Midwest. Fernway reference customer.",
            "Omaha, NE", "US", 900, 260, ["NetSuite"]),
    ];

    public static readonly NewsItem[] News =
    [
        new("N-101", "harborline.com", new(2026, 9, 10), "Harborline Logistics raises $85M Series C to expand cross-border freight",
            "The round will fund new cross-dock capacity and back-office modernization ahead of peak season.", "FreightWaves"),
        new("N-102", "harborline.com", new(2026, 9, 18), "Harborline is hiring 4 finance operations roles, including a Freight Audit Manager",
            "Job posts mention carrier invoice auditing, accruals and NetSuite experience.", "Harborline careers page"),
        new("N-103", "harborline.com", new(2026, 8, 2), "Harborline opens a Laredo cross-dock facility",
            "The 180,000 sq ft site handles northbound freight from Monterrey.", "Supply Chain Dive"),
        new("N-201", "quillstack.io", new(2026, 9, 3), "Quillstack launches usage-based billing for its CPQ platform",
            "Customers can now bill on metered usage; the company says billing operations volume has tripled.", "Quillstack blog"),
        new("N-301", "tidewaterhealth.es", new(2026, 7, 21), "Tidewater Health acquires two clinics in Alicante",
            "The acquisition brings the network to 14 sites.", "Levante-EMV"),
        new("N-401", "brightmark.com", new(2026, 8, 15), "Brightmark Retail names a new CFO",
            "Former grocery executive Dana Ruiz joins to lead a cost-reduction program.", "Columbus Business First"),
        new("N-501", "ardentfreight.com", new(2026, 6, 30), "Ardent Freight completes its move to NetSuite",
            "The carrier replaced its legacy Sage system to consolidate finance across three countries.", "Logistik Heute"),
    ];

    public static readonly ProofPoint[] ProofPoints =
    [
        new("P-1", "Kestrel Freight cut carrier invoice processing from 6 days to 1 day with Fernway.", "logistics"),
        new("P-2", "Fernway ships 300+ prebuilt connectors, including native NetSuite, SAP and Salesforce integrations.", null),
        new("P-3", "Fernway is SOC 2 Type II certified and offers EU data residency.", null),
        new("P-4", "Fernway's native NetSuite connector has been generally available since March 2026.", null),
        new("P-5", "Fernway customers automate quote-to-cash and billing operations alongside AP workflows.", "saas"),
    ];

    public static CrmData Create()
    {
        var data = new CrmData();

        data.Leads.AddRange(
        [
            new InboundLead
            {
                Id = "L-201", Name = "Dana Whitfield", Title = "VP of Operations", Email = "dana.whitfield@harborline.com",
                Company = "Harborline Logistics", Domain = "harborline.com", Country = "US", Source = "Demo request form",
                Message = "We're drowning in carrier invoices and want to automate reconciliation before peak season.",
                MarketingConsent = true, ReceivedOn = Today,
            },
            new InboundLead
            {
                Id = "L-202", Name = "Marcus Lee", Title = "Director of Revenue Operations", Email = "marcus.lee@quillstack.io",
                Company = "Quillstack", Domain = "quillstack.io", Country = "US", Source = "Webinar: Automating quote-to-cash",
                Message = "Can Fernway handle our billing operations too, not just AP?",
                MarketingConsent = true, ReceivedOn = Today,
            },
            new InboundLead
            {
                Id = "L-203", Name = "Sofia Marín", Title = "Chief Financial Officer", Email = "sofia.marin@tidewaterhealth.es",
                Company = "Tidewater Health", Domain = "tidewaterhealth.es", Country = "ES", Source = "Ebook download: Month-end close playbook",
                Message = "",
                MarketingConsent = false, ReceivedOn = Today.AddDays(-1),
            },
            new InboundLead
            {
                Id = "L-204", Name = "Kevin Osei", Title = "Student", Email = "kevin.osei.dev@gmail.com",
                Company = "", Domain = "gmail.com", Country = "US", Source = "Contact form",
                Message = "I'm doing a university project on automation. Can I get a free license?",
                MarketingConsent = true, ReceivedOn = Today.AddDays(-1),
            },
            new InboundLead
            {
                Id = "L-205", Name = "Ethan Cole", Title = "Director of Finance", Email = "ethan.cole@brightmark.com",
                Company = "Brightmark Retail", Domain = "brightmark.com", Country = "US", Source = "Trade show badge scan (list import)",
                Message = "",
                MarketingConsent = true, ReceivedOn = Today.AddDays(-2),
            },
            new InboundLead
            {
                Id = "L-206", Name = "Tom Becker", Title = "Chief Operating Officer", Email = "tom.becker@ardentfreight.com",
                Company = "Ardent Freight", Domain = "ardentfreight.com", Country = "DE", Source = "Demo request form",
                Message = "We looked at you last year. Revisiting now that we have moved to NetSuite.",
                MarketingConsent = true, ReceivedOn = Today.AddDays(-2),
            },
        ]);

        data.Accounts.AddRange(
        [
            new("A-310", "Quillstack", "quillstack.io", "Customer", "saas", "R-AR", "Nina Park", ["AP Automation"], 96000m, new(2024, 11, 18), new(2026, 12, 1)),
            new("A-322", "Ardent Freight", "ardentfreight.com", "Former prospect", "logistics", "R-LW", null, [], null, null, null),
            new("A-118", "Kestrel Freight", "kestrelfreight.com", "Customer", "logistics", "R-JB", "Omar Haddad", ["AP Automation", "Carrier Invoice Matching"], 142000m, new(2023, 4, 3), new(2027, 4, 3)),
        ]);

        data.Contacts.AddRange(
        [
            new("C-901", "A-310", "Grace Liu", "Corporate Controller (champion)", "grace.liu@quillstack.io"),
            new("C-902", "A-322", "Tom Becker", "Chief Operating Officer", "tom.becker@ardentfreight.com"),
            new("C-903", "A-118", "Hannah Voss", "VP Finance", "hannah.voss@kestrelfreight.com"),
        ]);

        data.Opportunities.AddRange(
        [
            new("O-501", "A-310", "Quillstack - AP Automation", "Closed Won", 96000m, "USD", new(2024, 11, 18), "R-AR", null),
            new("O-517", "A-322", "Ardent Freight - AP Automation", "Closed Lost", 38000m, "EUR", new(2025, 10, 14), "R-LW",
                "No native connector for their legacy Sage system; price concerns."),
        ]);

        data.Activities.AddRange(
        [
            new("ACT-7001", new(2026, 8, 20), "Meeting", "Quarterly business review",
                "Grace Liu happy with AP automation; mentioned billing team is overwhelmed after the usage-based pricing launch.", "A-310", null, "Nina Park"),
            new("ACT-7002", new(2025, 9, 16), "Meeting", "Discovery and demo",
                "Tom Becker liked approval workflows; blocker was the Sage integration.", "A-322", null, "Lukas Weber"),
        ]);

        data.DoNotContact.Add(new("ethan.cole@brightmark.com", new(2026, 3, 2), "Unsubscribed from all Fernway email"));
        return data;
    }
}
