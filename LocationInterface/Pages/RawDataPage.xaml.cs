﻿using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using LocationInterface.Utils;
using System.Threading;
using DatabaseManagerLibrary;
using AnalysisSDK;

namespace LocationInterface.Pages
{
    /// <summary>
    /// Interaction logic for RawDataPage.xaml
    /// </summary>
    public partial class RawDataPage : Page
    {
        protected Common Common { get; }

        /// <summary>
        /// Initialise the raw data viewer page
        /// </summary>
        /// <param name="common">Instance of the common calss</param>
        public RawDataPage(Common common)
        {
            InitializeComponent();

            Common = common;
        }

        /// <summary>
        /// Load the data from the selected tables
        /// </summary>
        public void LoadTables()
        {
            // Start a new background thread
            new Thread(() =>
            {
                // Clear the datagrid
                RawData.Dispatcher.Invoke(RawData.Items.Clear);
                // Initialise a location record buffer
                List<LocationRecord> locationRecordBuffer = new List<LocationRecord>();
                // Loop through each table in the loaded tables
                foreach (Table table in Common.LoadedDataTables)
                    // Loop through each record in the current table
                    foreach (Record currentLocationRecord in table.GetRecords())
                    {
                        // Add the current record to the buffer
                        locationRecordBuffer.Add(currentLocationRecord.ToObject<LocationRecord>());
                        // If the number of items in the buffer exceed the maximum buffer size add the current buffer to the datagrid
                        if (locationRecordBuffer.Count == SettingsManager.Active.RawDataRecordBuffer) PopulateDataGrid(ref locationRecordBuffer);
                    }
                // Populate the gird once more to ensure remaining records in the buffer are added
                PopulateDataGrid(ref locationRecordBuffer);
            })
            { IsBackground = true }.Start();
        }

        /// <summary>
        /// Add a buffer of location records to the wpf datagrid
        /// </summary>
        /// <param name="nextBuffer">A reference to a buffer list of location records</param>
        public void PopulateDataGrid(ref List<LocationRecord> nextBuffer)
        {
            // Create a local instance of the buffer
            List<LocationRecord> currentBuffer = nextBuffer;
            RawData.Dispatcher.Invoke(() =>
            {
                // Loop through each record in the buffer and add it to the datagird
                foreach (LocationRecord locationRecord in currentBuffer)
                    RawData.Items.Add(locationRecord);
            });
            // Clear the handled buffer
            nextBuffer.Clear();
        }

        private void RefreshTable(object sender, RoutedEventArgs e)
        {
            LoadTables();
        }
    }
}
