namespace HintOverlay
{
    /// <summary>
    /// UI Automation Control Type IDs
    /// </summary>
    internal static class UIA_ControlTypeIds
    {
        public const int UIA_ButtonControlTypeId = 50000;
        public const int UIA_CalendarControlTypeId = 50001;
        public const int UIA_CheckBoxControlTypeId = 50002;
        public const int UIA_ComboBoxControlTypeId = 50003;
        public const int UIA_EditControlTypeId = 50004;
        public const int UIA_HyperlinkControlTypeId = 50005;
        public const int UIA_ImageControlTypeId = 50006;
        public const int UIA_ListItemControlTypeId = 50007;
        public const int UIA_ListControlTypeId = 50008;
        public const int UIA_MenuControlTypeId = 50009;
        public const int UIA_MenuBarControlTypeId = 50010;
        public const int UIA_MenuItemControlTypeId = 50011;
        public const int UIA_ProgressBarControlTypeId = 50012;
        public const int UIA_RadioButtonControlTypeId = 50013;
        public const int UIA_ScrollBarControlTypeId = 50014;
        public const int UIA_SliderControlTypeId = 50015;
        public const int UIA_SpinnerControlTypeId = 50016;
        public const int UIA_StatusBarControlTypeId = 50017;
        public const int UIA_TabControlTypeId = 50018;
        public const int UIA_TabItemControlTypeId = 50019;
        public const int UIA_TextControlTypeId = 50020;
        public const int UIA_ToolBarControlTypeId = 50021;
        public const int UIA_ToolTipControlTypeId = 50022;
        public const int UIA_TreeItemControlTypeId = 50023;
        public const int UIA_TreeControlTypeId = 50024;
        public const int UIA_CustomControlTypeId = 50025;
        public const int UIA_GroupControlTypeId = 50026;
        public const int UIA_ThumbControlTypeId = 50027;
        public const int UIA_DataGridControlTypeId = 50028;
        public const int UIA_DataItemControlTypeId = 50029;
        public const int UIA_DocumentControlTypeId = 50030;
        public const int UIA_SplitButtonControlTypeId = 50031;
        public const int UIA_WindowControlTypeId = 50032;
        public const int UIA_PaneControlTypeId = 50033;
        public const int UIA_HeaderControlTypeId = 50034;
    }

    /// <summary>
    /// UI Automation Property IDs
    /// </summary>
    internal static class UIA_PropertyIds
    {
        public const int UIA_RuntimeIdPropertyId = 30000;
        public const int UIA_BoundingRectanglePropertyId = 30001;
        public const int UIA_ProcessIdPropertyId = 30002;
        public const int UIA_ControlTypePropertyId = 30003;
        public const int UIA_LocalizedControlTypePropertyId = 30004;
        public const int UIA_NamePropertyId = 30005;
        public const int UIA_AcceleratorKeyPropertyId = 30006;
        public const int UIA_AccessKeyPropertyId = 30007;
        public const int UIA_HasKeyboardFocusPropertyId = 30008;
        public const int UIA_IsKeyboardFocusablePropertyId = 30009;
        public const int UIA_IsEnabledPropertyId = 30010;
        public const int UIA_AutomationIdPropertyId = 30011;
        public const int UIA_ClassNamePropertyId = 30012;
        public const int UIA_HelpTextPropertyId = 30013;
        public const int UIA_ClickablePointPropertyId = 30014;
        public const int UIA_CulturePropertyId = 30015;
        public const int UIA_IsControlElementPropertyId = 30016;
        public const int UIA_IsContentElementPropertyId = 30017;
        public const int UIA_LabeledByPropertyId = 30018;
        public const int UIA_IsPasswordPropertyId = 30019;
        public const int UIA_NativeWindowHandlePropertyId = 30020;
        public const int UIA_ItemTypePropertyId = 30021;
        public const int UIA_IsOffscreenPropertyId = 30022;
        public const int UIA_OrientationPropertyId = 30023;
        public const int UIA_FrameworkIdPropertyId = 30024;
        public const int UIA_IsRequiredForFormPropertyId = 30025;
        public const int UIA_ItemStatusPropertyId = 30026;
        public const int UIA_IsDockPatternAvailablePropertyId = 30027;
        public const int UIA_IsExpandCollapsePatternAvailablePropertyId = 30028;
        public const int UIA_IsGridItemPatternAvailablePropertyId = 30029;
        public const int UIA_IsGridPatternAvailablePropertyId = 30030;
        public const int UIA_IsInvokePatternAvailablePropertyId = 30031;
        public const int UIA_IsMultipleViewPatternAvailablePropertyId = 30032;
        public const int UIA_IsRangeValuePatternAvailablePropertyId = 30033;
        public const int UIA_IsScrollPatternAvailablePropertyId = 30034;
        public const int UIA_IsScrollItemPatternAvailablePropertyId = 30035;
        public const int UIA_IsSelectionItemPatternAvailablePropertyId = 30036;
        public const int UIA_IsSelectionPatternAvailablePropertyId = 30037;
        public const int UIA_IsTablePatternAvailablePropertyId = 30038;
        public const int UIA_IsTableItemPatternAvailablePropertyId = 30039;
        public const int UIA_IsTextPatternAvailablePropertyId = 30040;
        public const int UIA_IsTogglePatternAvailablePropertyId = 30041;
        public const int UIA_IsTransformPatternAvailablePropertyId = 30042;
        public const int UIA_IsValuePatternAvailablePropertyId = 30043;
        public const int UIA_IsWindowPatternAvailablePropertyId = 30044;
        public const int UIA_ValueValuePropertyId = 30045;
        public const int UIA_ValueIsReadOnlyPropertyId = 30046;
        public const int UIA_RangeValueValuePropertyId = 30047;
        public const int UIA_RangeValueIsReadOnlyPropertyId = 30048;
        public const int UIA_RangeValueMinimumPropertyId = 30049;
        public const int UIA_RangeValueMaximumPropertyId = 30050;
        public const int UIA_RangeValueLargeChangePropertyId = 30051;
        public const int UIA_RangeValueSmallChangePropertyId = 30052;
        public const int UIA_ScrollHorizontalScrollPercentPropertyId = 30053;
        public const int UIA_ScrollHorizontalViewSizePropertyId = 30054;
        public const int UIA_ScrollVerticalScrollPercentPropertyId = 30055;
        public const int UIA_ScrollVerticalViewSizePropertyId = 30056;
        public const int UIA_ScrollHorizontallyScrollablePropertyId = 30057;
        public const int UIA_ScrollVerticallyScrollablePropertyId = 30058;
        public const int UIA_SelectionSelectionPropertyId = 30059;
        public const int UIA_SelectionCanSelectMultiplePropertyId = 30060;
        public const int UIA_SelectionIsSelectionRequiredPropertyId = 30061;
        public const int UIA_GridRowCountPropertyId = 30062;
        public const int UIA_GridColumnCountPropertyId = 30063;
        public const int UIA_GridItemRowPropertyId = 30064;
        public const int UIA_GridItemColumnPropertyId = 30065;
        public const int UIA_GridItemRowSpanPropertyId = 30066;
        public const int UIA_GridItemColumnSpanPropertyId = 30067;
        public const int UIA_GridItemContainingGridPropertyId = 30068;
        public const int UIA_DockDockPositionPropertyId = 30069;
        public const int UIA_ExpandCollapseExpandCollapseStatePropertyId = 30070;
        public const int UIA_MultipleViewCurrentViewPropertyId = 30071;
        public const int UIA_MultipleViewSupportedViewsPropertyId = 30072;
        public const int UIA_WindowCanMaximizePropertyId = 30073;
        public const int UIA_WindowCanMinimizePropertyId = 30074;
        public const int UIA_WindowWindowVisualStatePropertyId = 30075;
        public const int UIA_WindowWindowInteractionStatePropertyId = 30076;
        public const int UIA_WindowIsModalPropertyId = 30077;
        public const int UIA_WindowIsTopmostPropertyId = 30078;
        public const int UIA_SelectionItemIsSelectedPropertyId = 30079;
        public const int UIA_SelectionItemSelectionContainerPropertyId = 30080;
        public const int UIA_TableRowHeadersPropertyId = 30081;
        public const int UIA_TableColumnHeadersPropertyId = 30082;
        public const int UIA_TableRowOrColumnMajorPropertyId = 30083;
        public const int UIA_TableItemRowHeaderItemsPropertyId = 30084;
        public const int UIA_TableItemColumnHeaderItemsPropertyId = 30085;
        public const int UIA_ToggleToggleStatePropertyId = 30086;
        public const int UIA_TransformCanMovePropertyId = 30087;
        public const int UIA_TransformCanResizePropertyId = 30088;
        public const int UIA_TransformCanRotatePropertyId = 30089;
        public const int UIA_IsLegacyIAccessiblePatternAvailablePropertyId = 30090;
        public const int UIA_LegacyIAccessibleChildIdPropertyId = 30091;
        public const int UIA_LegacyIAccessibleNamePropertyId = 30092;
        public const int UIA_LegacyIAccessibleValuePropertyId = 30093;
        public const int UIA_LegacyIAccessibleDescriptionPropertyId = 30094;
        public const int UIA_LegacyIAccessibleRolePropertyId = 30095;
        public const int UIA_LegacyIAccessibleStatePropertyId = 30096;
        public const int UIA_LegacyIAccessibleHelpPropertyId = 30097;
        public const int UIA_LegacyIAccessibleKeyboardShortcutPropertyId = 30098;
        public const int UIA_LegacyIAccessibleSelectionPropertyId = 30099;
        public const int UIA_LegacyIAccessibleDefaultActionPropertyId = 30100;
    }

    /// <summary>
    /// UI Automation Pattern IDs
    /// </summary>
    internal static class UIA_PatternIds
    {
        public const int UIA_InvokePatternId = 10000;
        public const int UIA_SelectionPatternId = 10001;
        public const int UIA_ValuePatternId = 10002;
        public const int UIA_RangeValuePatternId = 10003;
        public const int UIA_ScrollPatternId = 10004;
        public const int UIA_ExpandCollapsePatternId = 10005;
        public const int UIA_GridPatternId = 10006;
        public const int UIA_GridItemPatternId = 10007;
        public const int UIA_MultipleViewPatternId = 10008;
        public const int UIA_WindowPatternId = 10009;
        public const int UIA_SelectionItemPatternId = 10010;
        public const int UIA_DockPatternId = 10011;
        public const int UIA_TablePatternId = 10012;
        public const int UIA_TableItemPatternId = 10013;
        public const int UIA_TextPatternId = 10014;
        public const int UIA_TogglePatternId = 10015;
        public const int UIA_TransformPatternId = 10016;
        public const int UIA_ScrollItemPatternId = 10017;
        public const int UIA_LegacyIAccessiblePatternId = 10018;
        public const int UIA_ItemContainerPatternId = 10019;
        public const int UIA_VirtualizedItemPatternId = 10020;
        public const int UIA_SynchronizedInputPatternId = 10021;
    }
}