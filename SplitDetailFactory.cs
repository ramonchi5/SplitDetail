// ============================================================================
// SplitDetailFactory.cs
// Registers SplitDetail with LiveSplit's component system.
//
// LiveSplit discovers components by scanning for IComponentFactory
// implementations exported from the assembly.
// ============================================================================

using System;
using System.Reflection;
using LiveSplit.Model;
using LiveSplit.UI.Components;

// This attribute tells LiveSplit's plugin loader which type is the factory.
[assembly: ComponentFactory(typeof(SplitDetailFactory))]

namespace LiveSplit.UI.Components
{
    public class SplitDetailFactory : IComponentFactory
    {
        // Display name shown in LiveSplit's "Add Component" dialog.
        public string ComponentName => "Split Detail";

        // Short description shown in the component list tooltip.
        public string Description =>
            "Shows timing details for current or previous splits and segments, " +
            "including subsplit support.";

        // Category determines which section of the component picker it appears in.
        public ComponentCategory Category => ComponentCategory.Information;

        // Creates a new instance of the component for a given LiveSplit state.
        public IComponent Create(LiveSplitState state)
            => new SplitDetailComponent(state);

        // ── Auto-update metadata ──────────────────────────────────────────────
        // Leave these empty if you are not hosting auto-update XML.
        // If you do host updates, point UpdateURL at your server root and
        // XMLURL at the component XML file.
        public string UpdateName => ComponentName;
        public string UpdateURL  => string.Empty;
        public string XMLURL     => string.Empty;

        public Version Version
            => Assembly.GetExecutingAssembly().GetName().Version;
    }
}
