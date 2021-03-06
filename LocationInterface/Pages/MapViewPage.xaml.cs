﻿using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using LocationInterface.Utils;
using System.Linq;
using DatabaseManagerLibrary;
using LocationInterface.Windows;
using AnalysisSDK;
using Microsoft.Xna.Framework;
using System.Threading;

namespace LocationInterface.Pages
{
    /// <summary>
    /// Interaction logic for MapViewPage.xaml
    /// </summary>
    public partial class MapViewPage : Page
    {
        protected Common Common { get; }
        protected ImageFile SelectedImageFile { get; set; }
        public List<ImageFileReference> ImageFileReferences { get; set; }
        protected MacPointCollection[] MacPointCollections { get; set; }
        public bool Polling { get; protected set; }
        protected bool TimeAutomation { get; set; }
        private SelectionManagerWindow SelectionManagerWindow { get; }
        private TimeManagerWindow TimeManagerWindow { get; }
        private TimeSetterWindow TimeSetterWindow { get; }
        private FollowManagerWindow FollowManagerWindow { get; }
        private MapperPluginWindow MapperPluginWindow { get; }
        private MacPointCollection[] FollowAddressPoints { get; set; }
        private bool UpdatingPoints { get; set; }

        public MapViewPage(Common common)
        {
            InitializeComponent();

            MacPointCollections = new MacPointCollection[0];
            FollowAddressPoints = new MacPointCollection[0];

            Common = common;
            TimeAutomation = false;
            
            ImageFileReferences = App.ImageIndex.ImageFileReferences;

            SelectionManagerWindow = new SelectionManagerWindow(Common, null);
            TimeSetterWindow = new TimeSetterWindow(UpdateTimedPoints);
            TimeManagerWindow = new TimeManagerWindow(TimeSetterWindow.TimeChange, TimeEnabledEvent, TimeDisabledEvent);
            FollowManagerWindow = new FollowManagerWindow();
            MapperPluginWindow = new MapperPluginWindow(selectedPlugins => MapViewer.LoadPlugins(selectedPlugins.Select(selectedPlugin => selectedPlugin.Mapper).ToArray()), MapViewer.UnloadPlugins);

            UpdateLocationIdentifierSelection();
            UpdateImageSelection();
        }

        private void UpdateImageSelection(string locationIdentifier = "")
        {
            MapImageSelector.SelectedIndex = -1;
            MapImageSelector.Items.Clear();
            foreach (ImageFileReference imageFileReference in ImageFileReferences.Where(i => string.IsNullOrWhiteSpace(locationIdentifier) || App.ImageIndex.GetImageFile(i).LocationIdentifier == locationIdentifier))
                    MapImageSelector.Items.Add(imageFileReference);
        }

        private void UpdateLocationIdentifierSelection()
        {
            List<string> locationIdentifiers = new List<string>();
            foreach (string locationIdentifier in ImageFileReferences.Select(i => App.ImageIndex.GetImageFile(i).LocationIdentifier))
                if (!locationIdentifiers.Contains(locationIdentifier))
                    locationIdentifiers.Add(locationIdentifier);

            LocationIdentifierSelector.SelectedIndex = -1;
            LocationIdentifierSelector.Items.Clear();
            foreach (string locationIdentifier in locationIdentifiers)
                LocationIdentifierSelector.Items.Add(locationIdentifier);
        }

        /// <summary>
        /// Clean up the map page
        /// </summary>
        public void OnClose()
        {
            // Close the non-dialog windows
            SelectionManagerWindow.Close();
            TimeManagerWindow.Close();
            TimeSetterWindow.Close();
        }

        /// <summary>
        /// Update all the points being used in the map-viewer
        /// </summary>
        public void UpdatePoints()
        {
            if (!UpdatingPoints)
            {
                UpdatePointsButton.IsEnabled = false;
                UpdatingPoints = true;

                new Thread(new ThreadStart(() =>
                {
                    // Check that there is a selected image file
                    // Get the selected MAC addresses
                    MacPointCollections = SelectionManagerWindow.Addresses.ToArray();

                    foreach (MacPointCollection collections in MacPointCollections)
                        foreach (string mapFileName in collections.MapLocationPoints.Keys)
                            collections.MapLocationPoints[mapFileName].Points.Clear();

                    if (Common.LoadedDataTables.Length > 0)
                        // Loop through each record in the current selected table where the deck
                        // is equal to the current deck map's ID
                        foreach (Record record in Common.LoadedDataTables[0].GetRecords())
                            // Loop through each selected MAC address
                            foreach (MacPointCollection macPointCollection in MacPointCollections)
                                // If the current deck MAC address is equal to the current selected
                                // MAC address
                                if (record.GetValue<string>("mac") == macPointCollection.Address)
                                {
                                    // Get the location record representation of the location point
                                    LocationRecord locationRecord = record.ToObject<LocationRecord>();
                                    foreach (string k in macPointCollection.MapLocationPoints.Keys.Where(k => macPointCollection.MapLocationPoints[k].LocationReference == locationRecord.Floor))
                                        macPointCollection.MapLocationPoints[k].Points.Add(new LocationPoint
                                        { Point = new Vector2((float)locationRecord.X, (float)locationRecord.Y), Node = locationRecord.Area, Time = locationRecord.Date.TimeOfDay });
                                    break;
                                }

                    Dispatcher.Invoke(() =>
                    {
                        UpdatePointsButton.IsEnabled = true;
                        MapViewer.LoadPoints(MacPointCollections);
                        UpdatingPoints = false;
                    });
                }
                ))
                { IsBackground = false }.Start();
            }
        }

        /// <summary>
        /// Update the map when changed
        /// </summary>
        /// <param name="sender">The control that called the action</param>
        /// <param name="e">Information about the selection change</param>
        private void MapChanged(object sender, SelectionChangedEventArgs e)
        {
            // Load the image in the map
            if (MapImageSelector.SelectedValue != null)
                MapViewer.LoadMap(SelectedImageFile = App.ImageIndex.GetImageFile(MapImageSelector.SelectedValue as ImageFileReference));
        }

        private void MapLocationChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateImageSelection(LocationIdentifierSelector.SelectedItem as string);
        }

        /// <summary>
        /// Update the timed points
        /// </summary>
        /// <param name="hour">The hour of the day to get points for</param>
        private void UpdateTimedPoints(double hour)
        {
            // Check that the time mode is enabled
            if (TimeManagerWindow.TimeEnabled && SelectedImageFile != null)
            {
                // Get a TimeSpan object for the selected hour
                TimeSpan selectedTime = TimeSpan.FromHours(hour);
                // Set the selected time label to a representation of the current selected time
                TimeManagerWindow.SelectedTimeLabel.Content = selectedTime.ToString(@"hh\:mm\:ss");

                if (FollowManagerWindow.FollowEnabled)
                {
                    IEnumerable<MacPointCollection> matchingCollections = MacPointCollections.Where(m => m.Address == FollowManagerWindow.SelectedAddress);
                    if (matchingCollections.Count() == 0) return;
                    MacPointCollection collection = matchingCollections.First();

                    TimeSpan latestTime = new TimeSpan();
                    LocationPoint currentPoint = null;
                    string fileName = "";
                    string locationIdentifier = "";

                    foreach (string locationFileName in collection.MapLocationPoints.Keys)
                        foreach (LocationPoint point in collection.MapLocationPoints[locationFileName].Points)
                            if (latestTime < point.Time && point.Time < selectedTime)
                            {
                                currentPoint = point;
                                fileName = locationFileName;
                                locationIdentifier = collection.MapLocationPoints[locationFileName].LocationReference;
                            }

                    if (currentPoint != null)
                    {
                        int currentIndex = ImageFileReferences.IndexOf(ImageFileReferences.Where(i => i.FileName == fileName).First());
                        if (MapImageSelector.SelectedIndex != currentIndex) MapImageSelector.SelectedIndex = currentIndex;
                        MapViewer.LoadPoints(new MacPointCollection[] { new MacPointCollection { Address = "", Colour = Microsoft.Xna.Framework.Color.Red, MapLocationPoints = new Dictionary<string, MapLocationPoint>() { { fileName, new MapLocationPoint(locationIdentifier, new List<LocationPoint>() { currentPoint }) } } } });
                    }
                    else
                        MapViewer.LoadPoints(new MacPointCollection[0]);
                }
                else
                {
                    // Produce a local MacPointCollection array to store the latest MacPointLocations
                    MacPointCollection[] macPointCollections = new MacPointCollection[MacPointCollections.Length];
                    // Pre-define the laterPoints (to temporarily store the points to be used)
                    LocationPoint[] laterPoints;
                    // Loop through the current macpointcollections
                    for (int i = 0; i < macPointCollections.Length; i++)
                    {
                        // Set the current temporary macPointCollections item with up to a single point that represents the most available location point
                        macPointCollections[i] = new MacPointCollection()
                        {
                            Address = MacPointCollections[i].Address,
                            Colour = MacPointCollections[i].Colour,
                        };

                        macPointCollections[i].MapLocationPoints[SelectedImageFile.FileName].Points =
                            (laterPoints = MacPointCollections[i].MapLocationPoints[SelectedImageFile.FileName].Points
                            .Where(point => point.Time < selectedTime).ToArray()).Length > 0 ? new List<LocationPoint> { laterPoints.Last() } : new List<LocationPoint>();
                    }

                    // Load these points into the map viewer
                    MapViewer.LoadPoints(macPointCollections);
                }
            }
        }

        /// <summary>
        /// Run when time-mode is enabled
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TimeEnabledEvent(object sender, RoutedEventArgs e)
        {
            TimeSetterWindow.TimeSlider.IsEnabled = MapViewer.TimeBased = true;
        }

        /// <summary>
        /// Run when time-mode is disabled
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TimeDisabledEvent(object sender, RoutedEventArgs e)
        {
            TimeSetterWindow.TimeSlider.IsEnabled = MapViewer.TimeBased = false;
            // Load all of the available points
            MapViewer.LoadPoints(MacPointCollections.ToArray());
        }

        /// <summary>
        /// Show the time management windows
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ShowTimeManager(object sender, RoutedEventArgs e)
        {
            TimeManagerWindow.Show();
            TimeSetterWindow.Show();
        }

        /// <summary>
        /// Hide the time management windows
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HideTimeManager(object sender, RoutedEventArgs e)
        {
            TimeManagerWindow.Hide();
            TimeSetterWindow.Hide();
        }

        /// <summary>
        /// Update all of the points
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UpdatePointsClick(object sender, RoutedEventArgs e)
        {
            UpdatePoints();
        }

        /// <summary>
        /// Show the MAC selection window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SelectDataClick(object sender, RoutedEventArgs e)
        {
            SelectionManagerWindow.Show();
        }

        private void ShowFollowManager(object sender, RoutedEventArgs e)
        {
            FollowManagerWindow.Show();
        }

        private void HideFollowManager(object sender, RoutedEventArgs e)
        {
            FollowManagerWindow.Hide();
        }

        private void ShowPluginManager(object sender, RoutedEventArgs e)
        {
            MapperPluginWindow.Show();
        }

        private void HidePluginManager(object sender, RoutedEventArgs e)
        {
            MapperPluginWindow.Hide();
        }

        private void ManageIndexClick(object sender, RoutedEventArgs e)
        {
            IndexManagerWindow indexManagerWindow = new IndexManagerWindow();
            if (indexManagerWindow.ShowDialog() == true)
            {
                ImageFileReferences = App.ImageIndex.ImageFileReferences;
                UpdateLocationIdentifierSelection();
                UpdateImageSelection(LocationIdentifierSelector.SelectedItem as string);
            }
        }
    }
}
