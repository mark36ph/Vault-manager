using System.Windows.Controls;

namespace FactVaultManager.Desktop;

public partial class MainShellWindow
{
    // Autopilot First UI owns the dashboard Scheduled text block under its original field name.
    // Keep schedule-target inventory updates decoupled from that legacy UI naming.
    private TextBlock? _autopilotScheduledCountText => _autopilotScheduleText;
}
