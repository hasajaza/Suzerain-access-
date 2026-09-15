using System;
using UnityEngine;
using UnityEngine.UI;

namespace SuzerainAccess.UI
{
    public enum ElementRole
    {
        Button, CheckBox, RadioButton, Tab, ToggleButton, Slider, ComboBox, EditField,
        Choice, DecisionOption, Option, Carousel, Article, Report, MapAction, Statistic,
        StatusEffect, Notification, Character, ExpandableGroup, Item
    }

    /// <summary>
    /// Accessibility representation of one real Unity UI control. Built by <see cref="ElementFactory"/>
    /// from the control's actual components; texts and states are read live every time they are spoken.
    /// </summary>
    internal sealed class AccessibleElement
    {
        public GameObject GameObject;
        public Selectable Selectable;
        public ElementRole Role;

        /// <summary>Visible name.</summary>
        public Func<string> Label = () => string.Empty;
        /// <summary>Current value (slider value, selected option, stat value...).</summary>
        public Func<string> Value = () => string.Empty;
        /// <summary>State words (checked, selected, new, read, expanded...).</summary>
        public Func<string> State = () => string.Empty;
        /// <summary>Extra detail read with "describe focused item" (tooltip texts, descriptions).</summary>
        public Func<string> Detail = () => string.Empty;
        /// <summary>Usage hint for High verbosity.</summary>
        public string Hint;

        public Action Activate;
        /// <summary>Change the value by a step (+1 / -1). Null when not adjustable.</summary>
        public Action<int> Adjust;

        /// <summary>When true, focusing this element must not move the game's EventSystem selection
        /// (text fields enter edit mode as soon as they are selected).</summary>
        public bool SuppressSelectionSync;

        /// <summary>The conversation's minimize button (ConversationPanel.minimizeButton).</summary>
        public bool IsConversationMinimize;

        /// <summary>The conversation's Continue button.</summary>
        public bool IsConversationContinue;

        /// <summary>A second-level tab (for example Policies / Situations inside an Overview category).</summary>
        public bool IsSubTab;

        /// <summary>The game's "new" marker on this control (NotificationIcon), if it has one.</summary>
        public NotificationIcon Notice;

        public IntPtr Key => GameObject != null ? GameObject.Pointer : IntPtr.Zero;

        /// <summary>Identity used to merge several selectables that belong to one logical control
        /// (for example the two arrow buttons of a carousel). Defaults to <see cref="Key"/>.</summary>
        public IntPtr Identity;

        public bool IsAlive => UiUtil.Alive(GameObject);

        public bool IsInteractable
        {
            get
            {
                try { return Selectable == null || Selectable.IsInteractable(); }
                catch { return false; }
            }
        }

        public static string RoleName(ElementRole role)
        {
            switch (role)
            {
                case ElementRole.Button: return "button";
                case ElementRole.CheckBox: return "check box";
                case ElementRole.RadioButton: return "radio button";
                case ElementRole.Tab: return "tab";
                case ElementRole.ToggleButton: return "toggle button";
                case ElementRole.Slider: return "slider";
                case ElementRole.ComboBox: return "combo box";
                case ElementRole.EditField: return "edit field";
                case ElementRole.Choice: return "response";
                case ElementRole.DecisionOption: return "decision option";
                case ElementRole.Option: return "option";
                case ElementRole.Carousel: return "selector";
                case ElementRole.Article: return "article";
                case ElementRole.Report: return "report";
                case ElementRole.MapAction: return "action";
                case ElementRole.Statistic: return "statistic";
                case ElementRole.StatusEffect: return "status effect";
                case ElementRole.Notification: return "notification";
                case ElementRole.Character: return "character";
                case ElementRole.ExpandableGroup: return "group";
                default: return "item";
            }
        }
    }
}
