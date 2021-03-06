// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using Axe.Windows.Actions.Contexts;
using AccessibilityInsights.CommonUxComponents.Controls;
using AccessibilityInsights.CommonUxComponents.Dialogs;
using Axe.Windows.Desktop.ColorContrastAnalyzer;
using Axe.Windows.Desktop.Utility;
using AccessibilityInsights.SharedUx.Dialogs;
using AccessibilityInsights.SharedUx.Interfaces;
using AccessibilityInsights.SharedUx.Settings;
using AccessibilityInsights.SharedUx.Telemetry;
using AccessibilityInsights.SharedUx.Utilities;
using AccessibilityInsights.SharedUx.ViewModels;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace AccessibilityInsights.SharedUx.Controls.TestTabs
{
    /// <summary>
    /// Color contrast user control
    /// 
    /// maintains two color choosers and computes color contrast between
    /// them when they are populated
    /// 
    /// maintains an eyedropper screenshot that is shared
    /// between the two color choosers
    /// </summary>
    public partial class ColorContrast : UserControl
    {
        const string HelpURL = @"https://go.microsoft.com/fwlink/?linkid=2075365";

        public ElementContext ElementContext { get; private set; }

        public ColorContrastViewModel ContrastVM { get; set; }

        public ColorContrast()
        {
            InitializeComponent();
            this.ContrastVM = new ColorContrastViewModel();

            // When user interacts with color chooser, reset selected element, hide pixel locations, and 
            //  begin recording if eyedropper action is selected
            this.firstChooser.ColorChangerInvoked += Chooser_ColorPickerClicked;
            this.secondChooser.ColorChangerInvoked += Chooser_ColorPickerClicked;

            // initial colors
            this.ContrastVM.FirstColor = System.Windows.Media.Colors.Black;
            this.ContrastVM.SecondColor = System.Windows.Media.Colors.White;
        }

        private void Dropper_Closed(object sender, EventArgs e)
        {
            CurrentChooser = null;
        }

        /// <summary>
        /// Set Highlighter button state in main UI
        /// this should be provided from Test Mode controller.
        /// </summary>
        public Action<bool> SetHighlighterButtonState { get; set; }

        private ColorChooser currentChooser;
        /// <summary>
        /// Reference to the current color chooser in use
        /// Makes border visible for current chooser
        /// </summary>
        private ColorChooser CurrentChooser
        {
            get
            {
                return currentChooser;
            }
            set
            {
                var oldChooser = currentChooser;
                currentChooser = value;
                if (oldChooser != null)
                {
                    oldChooser.BorderBrush = System.Windows.Media.Brushes.Transparent;
                }
                if (currentChooser != null)
                {
                    currentChooser.BorderBrush = System.Windows.Media.Brushes.Black;
                }
            }
        }

        /// <summary>
        /// For convenience, describes whether we are currently recording the first color or second color
        /// </summary>
        private bool SelectingFirstColor => currentChooser == firstChooser;
        private bool SelectingSecondColor => currentChooser == secondChooser;

        /// <summary>
        /// Sets element context and updates UI
        /// </summary>
        /// <param name="ec"></param>
        public void SetElement(ElementContext ec)
        {
            this.ElementContext = ec;
            var bm = ec.Element.CaptureBitmap();
            RunAutoCCA(bm);
        }

        private void RunAutoCCA(Bitmap bitmap)
        {
            var bmc = new BitmapCollection(bitmap);
            var result = bmc.RunColorContrastCalculation();
            var pair = result.GetMostLikelyColorPair();
            this.ContrastVM.FirstColor = pair.DarkerColor.DrawingColor.ToMediaColor();
            this.ContrastVM.SecondColor = pair.LighterColor.DrawingColor.ToMediaColor();
            tbConfidence.Text = result.ConfidenceValue().ToString();
        }

        private void RaiseLiveRegionEvents()
        {
            var peer = FrameworkElementAutomationPeer.FromElement(output) ?? new FrameworkElementAutomationPeer(output);
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            peer = FrameworkElementAutomationPeer.FromElement(tbConfidence) ?? new FrameworkElementAutomationPeer(tbConfidence);
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        /// <summary>
        /// When either chooser's color picker is clicked,
        /// we begin eyedropper color recording and set
        /// its target as the sender
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Chooser_ColorPickerClicked(object sender, SourceArgs e)
        {
            SetAutoCCAState(false);

            this.ContrastVM.BugId = null;
            if (e.Source == ColorChanger.Eyedropper)
            {
                CurrentChooser = sender as ColorChooser;
                var cc = new GlobalEyedropperWindow(ContrastVM, SelectingFirstColor, SelectingSecondColor, Dropper_Closed) { Owner = Application.Current.MainWindow };
                cc.Show();
            }
        }

        /// <summary>
        /// Reset color choosers to initial values and close eyedropper window
        /// </summary>
        public void ClearUI()
        {
            this.ElementContext = null;
            this.ContrastVM.Reset();
            this.firstChooser.Reset();
            this.secondChooser.Reset();
        }

        public object getConfidence()
        {
            return this.tbConfidence.Text;
        }

        public object getRatio()
        {
            return this.output.Text;
        }

        /// <summary>
        /// App configuration
        /// </summary>
        public static ConfigurationModel Configuration
        {
            get
            {
                return ConfigurationManager.GetDefaultInstance()?.AppConfig;
            }
        }

        /// <summary>
        /// Add horizontal scroll bars when necessary
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void scrollview_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var view = sender as ScrollViewer;
            if (e.NewSize.Width <= this.gridTabText.MinWidth)
            {
                view.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                this.gridTabText.Width = this.gridTabText.MinWidth;
            }
            else
            {
                view.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                this.gridTabText.Width = Double.NaN;
            }

            if (e.NewSize.Height <= this.gridTabText.MinHeight)
            {
                view.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                this.gridTabText.Height = this.gridTabText.MinHeight;
            }
            else
            {
                view.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                this.gridTabText.Height = Double.NaN;
            }
        }

        /// <summary>
        /// Overriding LocalizedControlType
        /// </summary>
        /// <returns></returns>
        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new CustomControlOverridingAutomationPeer(this, "pane");
        }

        /// <summary>
        /// Open the How to test link
        /// </summary>
        private void hlHowToTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(HelpURL));
            }
            catch (Exception ex)
            {
                ex.ReportException();
                MessageDialog.Show(string.Format(CultureInfo.InvariantCulture, Properties.Resources.ColorContrast_hlHowToTest_Click, HelpURL));
            }
        }

        private void TgbtnAutoDetect_Click(object sender, RoutedEventArgs e)
        {
            if (this.tgbtnAutoDetect.IsChecked.HasValue)
            {
                HandleToggleButtonSwitch(this.tgbtnAutoDetect.IsChecked.Value);
            }
        }

        private void HandleToggleButtonSwitch(bool isEnabled)
        {
            ICCAMode CCAMode = Application.Current.MainWindow as ICCAMode;

            // Watson crashes suggest that this will be null sometimes
            if (CCAMode!=null)
            {
                CCAMode.HandleToggleStatusChanged(isEnabled);
            }

            if (isEnabled)
            {
                spConfidence.Visibility = Visibility.Visible;
            }
            else
            {
                spConfidence.Visibility = Visibility.Hidden;
                tbConfidence.Text = "";
            }
        }

        public void SetAutoCCAState(bool state)
        {
            if (this.tgbtnAutoDetect.IsChecked.Value != state)
            {
                this.tgbtnAutoDetect.IsChecked = state;
                HandleToggleButtonSwitch(state);
            }
        }

        public bool IsToggleChecked()
        {
            return this.tgbtnAutoDetect.IsChecked.Value;
        }

        public void ActivateProgressRing(){
            this.ctrlProgressRing.Activate();
        }

        public void DeactivateProgressRing()
        {
            this.ctrlProgressRing.Deactivate();
        }

    }
}
