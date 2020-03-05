﻿using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Collections.Generic;

namespace ClipChopper.DesktopApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private _selectedDirectory;
        private string? _loadedMedia;
        private FragmentSelection? _fragment;


        public MainWindow()
        {
            InitializeComponent();
            Media.MediaOpened += Media_MediaOpened;
        }

        private void Media_MediaOpened(object sender, Unosquare.FFME.Common.MediaOpenedEventArgs e)
        {
            Console.WriteLine(e.Info.Duration);
            Save.IsEnabled = true;
            _fragment = new FragmentSelection(e.Info.Duration);
            PositionSlider.SelectionStart = _fragment.Start.TotalSeconds;
            PositionSlider.SelectionEnd = _fragment.Stop.TotalSeconds;
            _fragment.PropertyChanged += Fragment_PropertyChanged;
        }

        private void Fragment_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_fragment is null)
            {
                throw new InvalidOperationException("Fragment value is not initialized.");
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(e.PropertyName, "Start"))
            {
                PositionSlider.SelectionStart = _fragment.Start.TotalSeconds;
            }
            else
            {
                PositionSlider.SelectionEnd = _fragment.Stop.TotalSeconds;
            }
        }

        private async void Play_Click(object sender, RoutedEventArgs e)
        {

            if (Media.IsPlaying)
            {
                await Media.Pause();
            }
            else
            {
                if (Media.HasMediaEnded)
                {
                    await Media.Seek(TimeSpan.Zero);
                }
                await Media.Play();
            }
        }

        private void Pframe_Click(object sender, RoutedEventArgs e)
        {
            Media.StepBackward();
        }

        private void Nframe_Click(object sender, RoutedEventArgs e)
        {
            // Don't make a step if current frame is the last one
            // fixes an issue when StepForward actually moves to a previous key frame
            if (Media.Position + Media.PositionStep < Media.PlaybackEndTime)
            {
                Media.StepForward();
            }
        }

        private void SelectDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                _selectedDirectory = dialog.SelectedPath;

                // TODO: implement extensions filter, move this to new method.
                IReadOnlyList<string> files = Directory.GetFiles(_selectedDirectory, "*.*")
                    .Where(s => s.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                DirectoryList.ItemsSource = Enumerable.Range(0, files.Count)
                    .Select(i => new DirectoryItem(files[i]))
                    .ToList();
            }
        }

        private void RefreshDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory is null) return;

            // TODO: implement extensions filter, move this to new method.
            List<string> files = Directory.GetFiles(_selectedDirectory, "*.*")
                .Where(s => s.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
                .ToList();

            DirectoryList.ItemsSource = Enumerable.Range(0, files.Count).Select(i => new DirectoryItem(files[i])).ToList();
            DirectoryList.SelectedIndex = files.IndexOf(_loadedMedia);
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_fragment is null) return;

            _fragment.Start = Media.Position;
            Debug.WriteLine(Media.Position);
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_fragment is null) return;

            _fragment.Stop = Media.Position;
        }

        private void DirectoryList_Selected(object sender, SelectionChangedEventArgs args)
        {
            if (DirectoryList.SelectedItem is null ||
                ((DirectoryItem) DirectoryList.SelectedItem).Path == _loadedMedia)
            {
                // Do nothing if selection disappeared or selected file is already loaded.
                return;
            }

            var selectedFile = (DirectoryItem) DirectoryList.SelectedItem;
            if (!File.Exists(selectedFile.Path))
            {
                MessageBox.Show($"Could not load non-existant file {selectedFile.Path}");
                return;
            }

            Media.Open(new Uri(selectedFile.Path));
            _loadedMedia = selectedFile.Path;
        }

        private void Save_Click(object sender, RoutedEventArgs eventArgs)
        {
            if (_fragment is null) return;

            if (_loadedMedia is null)
            {
                throw new InvalidOperationException("Loaded media value is not initialized.");
            }
            Debug.WriteLine(Path.GetExtension(_loadedMedia).Substring(1, 3));

            var dialog = new Ookii.Dialogs.Wpf.VistaSaveFileDialog()
            {
                AddExtension = true,
                Filter = "MP4 Files (*.mp4)|*.mp4|MKV Files (*.mkv)|*.mkv",
                DefaultExt = "mp4",
                Title = "Save a clip",
                OverwritePrompt = true,
                FileName = "Trimmed " + Path.GetFileName(_loadedMedia)
            };
            
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                Console.WriteLine(dialog.FileName);
                var inputFile = _loadedMedia;
                var outputFile = dialog.FileName;

                var ffmpegPath = Path.Combine(Unosquare.FFME.Library.FFmpegDirectory, "ffmpeg.exe");
                var startKeyframe = KeyframeProber.FindClosestKeyframeTime(inputFile, _fragment.Start);

                string args = $"-ss {startKeyframe} -i \"{inputFile}\" -map_metadata 0 " +
                    $"-to \"{_fragment.Stop - startKeyframe}\" -c:v copy -c:a copy " +
                    $"-map 0 \"{outputFile}\"";

                var startInfo = new ProcessStartInfo()
                {
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    FileName = ffmpegPath,
                    Arguments = args
                };

                using (var ffmpeg = Process.Start(startInfo))
                {
                    ffmpeg.OutputDataReceived += new DataReceivedEventHandler((s, e) =>
                    {
                        Debug.WriteLine(e.Data);
                    });
                    ffmpeg.WaitForExit();
                }
                MessageBox.Show("Done");
            }
        }
    }
}