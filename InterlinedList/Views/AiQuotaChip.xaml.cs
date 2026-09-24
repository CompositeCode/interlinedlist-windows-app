using System.Windows.Controls;
using InterlinedList.Services;

namespace InterlinedList.Views;

/// <summary>
/// The remaining-daily-quota chip #10 asks for wherever an AI action is
/// offered. Drop it in with no attributes and no code-behind in the host:
///
/// <code>
/// &lt;local:AiQuotaChip/&gt;
/// </code>
///
/// It reads <see cref="AiAvailabilityService.Shared"/> directly rather than
/// taking a dependency property, because there is exactly one answer per
/// session and every instance wants the same one. It also hides itself when the
/// gate is closed, so a host never needs its own visibility binding for it.
///
/// It binds <c>QuotaLabel</c>, which is derived from
/// <see cref="Models.AiQuota.RemainingOrComputed"/> — <b>not</b> from
/// <c>AiQuota.Remaining</c>. That matters: <c>/api/ai/status</c> sends
/// <c>remaining</c> but <c>/api/ai/suggest</c> does not (verified live), so
/// after a suggestion the raw field is null and only the computed one is right.
/// </summary>
public partial class AiQuotaChip : UserControl
{
    public AiQuotaChip()
    {
        InitializeComponent();
        DataContext = AiAvailabilityService.Shared;

        // Cheap safety net: if this chip is the first AI surface a user reaches,
        // make sure the once-per-session status fetch has been kicked off.
        // EnsureLoadedAsync is idempotent and single-flighted.
        _ = AiAvailabilityService.Shared.EnsureLoadedAsync();
    }
}
