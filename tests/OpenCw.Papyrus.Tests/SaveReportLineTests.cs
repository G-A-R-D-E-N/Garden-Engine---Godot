using OpenCommonwealth.Services.Papyrus.Save;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class SaveReportLineTests
{
    [Fact]
    public void The_quest_script_restore_line_names_every_count_in_order()
    {
        Assert.Equal(
            "quest scripts: wrote 1 propert(ies), reset 2 to initial values, dropped 3 quest(s), 4 script(s) and 5 propert(ies) not in this session, 6 started quest(s) not in the save",
            new QuestScriptRestoreReport(1, 2, 3, 4, 5, 6).ToLine());
    }

    [Fact]
    public void The_papyrus_state_restore_line_names_every_count_in_order()
    {
        Assert.Equal(
            "papyrus state: restored 5 variable(s) on 2 instance(s), skipped 1 instance(s) and 3 variable(s) not in this session, 4 reference(s) left unbound",
            new PapyrusStateRestoreReport(2, 5, 1, 3, 4, null).ToLine());
    }

    [Fact]
    public void A_refused_papyrus_state_restore_line_carries_only_the_reason()
    {
        Assert.Equal(
            "papyrus state: refused, instance state left as started: the session has no Radicle host",
            new PapyrusStateRestoreReport(0, 0, 0, 0, 0, "the session has no Radicle host").ToLine());
    }
}
