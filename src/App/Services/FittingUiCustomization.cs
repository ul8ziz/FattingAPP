using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ul8ziz.FittingApp.Device.DeviceCommunication.Models;

namespace Ul8ziz.FittingApp.App.Services
{
    /// <summary>
    /// Centralised UI-customisation layer for the Fitting screen.
    /// Applies tab renames, parameter renames, hide rules, read-only overrides,
    /// and whitelist filtering to a <see cref="DeviceSettingsSnapshot"/> after it
    /// has been loaded from the device/library.
    ///
    /// Rules are keyed by stable SDK identifiers (Parameter.Name / SettingCategory.Id / Title)
    /// so they work across products (E7111, E7160, Eargate, etc.).
    /// </summary>
    public static class FittingUiCustomization
    {
        // =====================================================================
        //  Tab (category) renames  –  key = original Title OR SanitizedId
        // =====================================================================
        private static readonly Dictionary<string, string> TabRenames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Application"]  = "General Volume Setting",
            ["AGCo"]         = "Output & Safety Limits",
            ["Framework"]    = "Active Memories",
            ["SG"]           = "Sound Generator",
            ["S G"]          = "Sound Generator",
        };

        // =====================================================================
        //  Parameter display-name renames  –  key = SettingItem.Name (SDK name)
        // =====================================================================
        private static readonly Dictionary<string, string> ParamRenames = new(StringComparer.OrdinalIgnoreCase)
        {
            // Application tab
            ["X_VolumeDefault"]            = "Startup Volume",
            ["X_VolumeStep"]               = "Volume Step Size",
            ["X_VolumeMinimum"]            = "Minimum Volume Limit",
            ["X_MemoryRetention_Enable"]   = "Remember Last Program",
            ["X_VolumeRetention_Enable"]   = "Remember Last Volume",

            // AGCo tab
            ["X_AGCo_WidebandOutputLimit"] = "Output Limit",
            ["X_AGCo_WidebandGain"]        = "Overall Gain",
        };

        // =====================================================================
        //  Hidden parameters  –  matched by prefix or exact name
        // =====================================================================
        private static readonly List<string> HiddenPrefixes = new()
        {
            // NR array hidden in Fitting (shown as single slider in Quick Fitting)
            "X_NR_MaxDepth[",
            "X_NR_EstimateTimeConst[",

            // Framework: Memory identifiers hidden
            "X_FWK_MemoryIdentifier[",

            // EQ Enable hidden
            "X_EQ_Enable",

            // MMI: I/O DIO pads hidden
            "X_VolumeUpDIO",
            "X_VolumeDownDIO",
            "X_ProfileUpDIO",
            "X_ProfileDownDIO",
            "X_SingleButtonVolumeDIO",
            "X_TelecoilOnDIO",
            "X_TelecoilOffDIO",
            "X_PowerOffButtonDIO",
            "X_PowerOffDsEnDIO",
            "X_AnalogVolumeControlDIO",
            "X_ARD_DIO",

            // MMI: power off / HPM10
            "X_PowerOffEvent",
            "X_PowerOffTrigger",
            "X_HPM10WarnDIO",
            "X_CaseDetectDIO",

            // MMI: right panel durations / detection
            "X_MMI_DetectionEdge",
            "X_MMI_LongPressDuration",
            "X_MMI_VeryLongPressDuration",
            "X_MMI_SuperLongPressDuration",
            "X_MMI_ToggleSwitchLowDuration",
            "X_MMI_ToggleSwitchHighDuration",

            // NR response rate hidden
            "X_NR_ResponseRate",

            // WDRC: expansion rows hidden
            "X_WDRC_ExpansionAttackTime[",
            "X_WDRC_ExpansionEnable[",
            "X_WDRC_ExpansionRatio[",
            "X_WDRC_ExpansionThreshold[",
            "X_WDRC_ExpansionReleaseTime[",
            // WDRC: compressor attack/release hidden
            "X_WDRC_CompressorAttackTime[",
            "X_WDRC_CompressorReleaseTime[",
            // WDRC: AGCo attack/release hidden
            "X_WDRC_AGCoAttackTime[",
            "X_WDRC_AGCoReleaseTime[",
            // WDRC: lower/upper thresholds hidden
            "X_WDRC_LowerThreshold[",
            "X_WDRC_UpperThreshold[",
        };

        // =====================================================================
        //  Read-only overrides  –  parameters that show value but can't be edited
        // =====================================================================
        private static readonly List<string> ReadOnlyPrefixes = new()
        {
            "X_EQ_CrossoverFrequency[",
        };

        // =====================================================================
        //  Tab whitelists  –  only listed param prefixes are shown for these tabs
        // =====================================================================
        private static readonly Dictionary<string, List<string>> TabWhitelists = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Sound Generator"] = new()
            {
                "X_SG_EnableMode",
                "X_SG_Level",
                "X_SG_CycleTime",
                "X_SG_WaveDepth",
                "X_SG_RandomizationPercent",
            },
            ["SG"] = new()
            {
                "X_SG_EnableMode",
                "X_SG_Level",
                "X_SG_CycleTime",
                "X_SG_WaveDepth",
                "X_SG_RandomizationPercent",
            },
        };

        // Feedback Measurement: show only Run and Export-related params.
        // Exact SDK param names are product-specific; match by display name keywords.
        private static readonly HashSet<string> FeedbackMeasurementTabs = new(StringComparer.OrdinalIgnoreCase)
        {
            "Feedback Measurement",
            "FeedbackMeasurement",
        };
        private static readonly string[] FeedbackMeasurementAllowedKeywords = { "run", "export" };

        // =====================================================================
        //  Public API
        // =====================================================================

        /// <summary>
        /// Applies all UI customisations (renames, read-only, hide markers) to a snapshot
        /// in-place. Call once after loading from device/library, before building tabs/groups.
        /// </summary>
        public static void ApplyToSnapshot(DeviceSettingsSnapshot? snapshot)
        {
            if (snapshot == null) return;

            foreach (var cat in snapshot.Categories)
            {
                // Rename tabs
                if (TabRenames.TryGetValue(cat.Title, out var newTitle))
                    cat.Title = newTitle;
                else if (TabRenames.TryGetValue(cat.Id, out var newTitleById))
                    cat.Title = newTitleById;

                foreach (var sec in cat.Sections)
                {
                    foreach (var item in sec.Items)
                    {
                        // Rename parameters
                        if (ParamRenames.TryGetValue(item.Name, out var newDisplay))
                            item.DisplayName = newDisplay;

                        // Read-only overrides
                        if (MatchesAnyPrefix(item.Name, ReadOnlyPrefixes))
                            item.ReadOnly = true;
                    }
                }
            }

            Debug.WriteLine("[FittingUiCustomization] Applied renames + read-only overrides to snapshot");
        }

        /// <summary>
        /// Returns true if the parameter should be hidden from the normal Fitting tab view.
        /// Called from <c>BuildRowsForGroup</c> during row construction.
        /// </summary>
        public static bool IsHidden(SettingItem item)
        {
            return MatchesAnyPrefix(item.Name, HiddenPrefixes);
        }

        /// <summary>
        /// Returns true if the parameter should be hidden from the normal Fitting tab view,
        /// taking the tab context into account (for whitelist filtering).
        /// </summary>
        public static bool IsHiddenInTab(SettingItem item, string tabTitle)
        {
            // General hidden rules
            if (IsHidden(item)) return true;

            // Tab whitelist: if the tab has a whitelist, only show listed params
            if (TabWhitelists.TryGetValue(tabTitle, out var whitelist))
            {
                return !whitelist.Any(prefix =>
                    item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }

            // Feedback Measurement: display-name keyword whitelist
            if (FeedbackMeasurementTabs.Contains(tabTitle))
            {
                var display = (item.DisplayName ?? item.Name ?? "").ToLowerInvariant();
                return !FeedbackMeasurementAllowedKeywords.Any(kw => display.Contains(kw));
            }

            return false;
        }

        /// <summary>
        /// Returns the visible parameter count for a section after applying hide rules.
        /// Used to update <c>GroupDescriptor.ParamsCount</c> so the badge reflects the real count.
        /// </summary>
        public static int CountVisibleParams(SettingSection section, string tabTitle)
        {
            int count = 0;
            foreach (var item in section.Items)
            {
                if (!IsHiddenInTab(item, tabTitle))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Resolves the display title for a tab, applying any rename rules.
        /// </summary>
        public static string ResolveTabTitle(string originalTitle, string sanitizedId)
        {
            if (TabRenames.TryGetValue(originalTitle, out var renamed))
                return renamed;
            if (TabRenames.TryGetValue(sanitizedId, out var renamedById))
                return renamedById;
            return originalTitle;
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static bool MatchesAnyPrefix(string? name, List<string> prefixes)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var prefix in prefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (name.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
