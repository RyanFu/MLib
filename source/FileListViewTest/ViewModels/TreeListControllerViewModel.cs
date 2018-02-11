namespace FileListViewTest.ViewModels
{
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Input;
    using FileListView.Interfaces;
    using FileListViewTest.Command;
    using FileListViewTest.Interfaces;
    using FileSystemModels;
    using FileSystemModels.Browse;
    using FileSystemModels.Events;
    using FileSystemModels.Interfaces;
    using FileSystemModels.Interfaces.Bookmark;
    using FileSystemModels.Models;
    using FilterControlsLib.Interfaces;
    using FolderBrowser.Interfaces;
    using FolderControlsLib.Interfaces;

    /// <summary>
    /// Class implements a tree/folder/file view model class
    /// that can be used to dispaly filesystem related content in a view or dialog.
    /// 
    /// Common Sample dialogs are file pickers for load/save etc.
    /// </summary>
    internal class TreeListControllerViewModel : Base.ViewModelBase, ITreeListControllerViewModel
    {
        #region fields
        private SemaphoreSlim SlowStuffSemaphore = null;

        private string _SelectedFolder = string.Empty;
        private object _LockObject = new object();
        private ICommand _RefreshCommand;
        #endregion fields

        #region constructor
        /// <summary>
        /// Custom class constructor
        /// </summary>
        /// <param name="onFileOpenMethod"></param>
        public TreeListControllerViewModel(System.EventHandler<FileOpenEventArgs> onFileOpenMethod)
          : this()
        {
            // Remove the standard constructor event that is fired when a user opens a file
            this.FolderItemsView.OnFileOpen -= this.FolderItemsView_OnFileOpen;

            // ...and establish a new link (if any)
            if (onFileOpenMethod != null)
                this.FolderItemsView.OnFileOpen += onFileOpenMethod;
        }

        /// <summary>
        /// Class constructor
        /// </summary>
        public TreeListControllerViewModel()
        {
            SlowStuffSemaphore = new SemaphoreSlim(1, 1);

            FolderItemsView = FileListView.Factory.CreateFileListViewModel(new BrowseNavigation());
            FolderTextPath = FolderControlsLib.Factory.CreateFolderComboBoxVM();
            RecentFolders = FileSystemModels.Factory.CreateBookmarksViewModel();
            TreeBrowser = FolderBrowser.FolderBrowserFactory.CreateBrowserViewModel(false);

            // This is fired when the user selects a new folder bookmark from the drop down button
            RecentFolders.BrowseEvent += FolderTextPath_BrowseEvent;

            // This is fired when the text path in the combobox changes to another existing folder
            FolderTextPath.BrowseEvent += FolderTextPath_BrowseEvent;

            Filters = FilterControlsLib.Factory.CreateFilterComboBoxViewModel();
            Filters.OnFilterChanged += this.FileViewFilter_Changed;

            // This is fired when the current folder in the listview changes to another existing folder
            this.FolderItemsView.BrowseEvent += FolderTextPath_BrowseEvent;

            // Event fires when the user requests to add a folder into the list of recently visited folders
            this.FolderItemsView.BookmarkFolder.RequestEditBookmarkedFolders += this.FolderItemsView_RequestEditBookmarkedFolders;

            // This event is fired when a user opens a file
            this.FolderItemsView.OnFileOpen += this.FolderItemsView_OnFileOpen;

            TreeBrowser.BrowseEvent += FolderTextPath_BrowseEvent;

            // Event fires when the user requests to add a folder into the list of recently visited folders
            TreeBrowser.BookmarkFolder.RequestEditBookmarkedFolders += this.FolderItemsView_RequestEditBookmarkedFolders;
        }
        #endregion constructor

        #region properties
        /// <summary>
        /// Gets the command that updates the currently viewed
        /// list of directory items (files and sub-directories).
        /// </summary>
        public ICommand RefreshCommand
        {
            get
            {
                if (this._RefreshCommand == null)
                    this._RefreshCommand = new RelayCommand<object>
                        ((p) =>
                        {
                            RefreshCommand_Executed(p as string);
                        });

                return this._RefreshCommand;
            }
        }

        /// <summary>
        /// Gets the currently selected recent location string (if any) or null.
        /// </summary>
        public string SelectedRecentLocation
        {
            get
            {
                if (this.RecentFolders != null)
                {
                    if (this.RecentFolders.SelectedItem != null)
                        return this.RecentFolders.SelectedItem.FullPath;
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the viewmodel that exposes recently visited locations (bookmarked folders).
        /// </summary>
        public IBookmarksViewModel RecentFolders { get; }

        /// <summary>
        /// Expose a viewmodel that can represent a Folder-ComboBox drop down
        /// element similar to a web browser Uri drop down control but using
        /// local paths only.
        /// </summary>
        public IFolderComboBoxViewModel FolderTextPath { get; }

        /// <summary>
        /// Expose a viewmodel that can represent a Filter-ComboBox drop down
        /// similar to the top-right filter/search combo box in Windows Exploer windows.
        /// </summary>
        public IFilterComboBoxViewModel Filters { get; }

        /// <summary>
        /// Expose a viewmodel that can support a listview showing folders and files
        /// with their system specific icon.
        /// </summary>
        public IFileListViewModel FolderItemsView { get; }

        /// <summary>
        /// Gets the viewmodel that drives the folder picker control.
        /// </summary>
        public IBrowserViewModel TreeBrowser { get;  }

        /// <summary>
        /// Gets the currently selected folder path string.
        /// </summary>
        public string SelectedFolder
        {
            get
            {
                return this._SelectedFolder;
            }

            private set
            {
                if (this._SelectedFolder != value)
                {
                    this._SelectedFolder = value;
                    base.NotifyPropertyChanged(() => this.SelectedFolder);
                }
            }
        }
        #endregion properties

        #region methods
        #region Explorer settings model
        /// <summary>
        /// Configure this viewmodel (+ attached browser viewmodel) with the given settings and
        /// initialize viewmodels with UserProfile.CurrentPath location.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        bool IConfigExplorerSettings.ConfigureExplorerSettings(ExplorerSettingsModel settings)
        {
            if (settings == null)
                return false;

            if (settings.UserProfile == null)
                return false;

            if (settings.UserProfile.CurrentPath == null)
                return false;

            try
            {
                // Set currently viewed folder in Explorer Tool Window
                var t = new Task(async () =>
                {
                    await this.NavigateToFolderAsync(settings.UserProfile.CurrentPath, null);
                });

                t.RunSynchronously();

                this.Filters.ClearFilter();

                // Set file filter in file/folder list view
                foreach (var item in settings.FilterCollection)
                    this.Filters.AddFilter(item.FilterDisplayName, item.FilterText);

                // Add a current filter setting (if any is available)
                if (settings.UserProfile.CurrentFilter != null)
                {
                    this.Filters.SetCurrentFilter(settings.UserProfile.CurrentFilter.FilterDisplayName,
                                                  settings.UserProfile.CurrentFilter.FilterText);
                }

                this.RecentFolders.ClearFolderCollection();

                // Set collection of recent folder locations
                foreach (var item in settings.RecentFolders)
                    this.RecentFolders.AddFolder(item);

                this.FolderItemsView.SetShowIcons(settings.ShowIcons);
                this.FolderItemsView.SetIsFolderVisible(settings.ShowFolders);
                this.FolderItemsView.SetShowHidden(settings.ShowHiddenFiles);
                this.FolderItemsView.SetIsFiltered(settings.IsFiltered);
            }
            catch
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Compare given <paramref name="input"/> settings with current settings
        /// and return a new settings model if the current settings
        /// changed in comparison to the given <paramref name="input"/> settings.
        /// 
        /// Always return current settings if <paramref name="input"/> settings is null.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        ExplorerSettingsModel IConfigExplorerSettings.GetExplorerSettings(ExplorerSettingsModel input)
        {
            var settings = new ExplorerSettingsModel();

            try
            {
                settings.UserProfile.SetCurrentPath(this.SelectedFolder);

                foreach (var item in settings.FilterCollection)
                {
                    if (item == settings.UserProfile.CurrentFilter)
                        this.AddFilter(item.FilterDisplayName, item.FilterText, true);
                    else
                        this.AddFilter(item.FilterDisplayName, item.FilterText);
                }

                foreach (var item in this.Filters.CurrentItems)
                {
                    if (item == this.Filters.SelectedItem)
                        settings.AddFilter(item.FilterDisplayName, item.FilterText, true);
                    else
                        settings.AddFilter(item.FilterDisplayName, item.FilterText);
                }

                foreach (var item in this.RecentFolders.DropDownItems)
                    settings.AddRecentFolder(item.FullPath);

                if (string.IsNullOrEmpty(this.SelectedRecentLocation) == false)
                {
                    settings.LastSelectedRecentFolder = this.SelectedRecentLocation;
                    settings.AddRecentFolder(this.SelectedRecentLocation);
                }

                settings.ShowIcons = this.FolderItemsView.ShowIcons;
                settings.ShowFolders = this.FolderItemsView.ShowFolders;
                settings.ShowHiddenFiles = this.FolderItemsView.ShowHidden;
                settings.IsFiltered = this.FolderItemsView.IsFiltered;

                if (ExplorerSettingsModel.CompareSettings(input, settings) == false)
                    return settings;
                else
                    return null;
            }
            catch
            {
                throw;
            }
        }
        #endregion Explorer settings model

        #region Bookmarked Folders Methods
        /// <summary>
        /// Add a recent folder location into the collection of recent folders.
        /// This collection can then be used in the folder combobox drop down
        /// list to store user specific customized folder short-cuts.
        /// </summary>
        /// <param name="folderPath"></param>
        /// <param name="selectNewFolder"></param>
        public void AddRecentFolder(string folderPath, bool selectNewFolder = false)
        {
            this.RecentFolders.AddFolder(folderPath, selectNewFolder);
        }

        /// <summary>
        /// Removes a recent folder location into the collection of recent folders.
        /// This collection can then be used in the folder combobox drop down
        /// list to store user specific customized folder short-cuts.
        /// </summary>
        /// <param name="path"></param>
        public void RemoveRecentFolder(string path)
        {
            if (string.IsNullOrEmpty(path) == true)
                return;

            this.RecentFolders.RemoveFolder(path);
        }

        /// <summary>
        /// Copies all of the given bookmars into the destionations bookmarks collection.
        /// </summary>
        /// <param name="srcRecentFolders"></param>
        /// <param name="dstRecentFolders"></param>
        public void CloneBookmarks(IBookmarksViewModel srcRecentFolders,
                                   IBookmarksViewModel dstRecentFolders)
        {
            if (srcRecentFolders == null || dstRecentFolders == null)
                return;

            dstRecentFolders.ClearFolderCollection();

            // Set collection of recent folder locations
            foreach (var item in srcRecentFolders.DropDownItems)
                dstRecentFolders.AddFolder(item.FullPath);
        }
        #endregion Bookmarked Folders Methods

        #region Change filter methods
        /// <summary>
        /// Add a new filter argument to the list of filters and
        /// select this filter if <paramref name="bSelectNewFilter"/>
        /// indicates it.
        /// </summary>
        /// <param name="filterString"></param>
        /// <param name="bSelectNewFilter"></param>
        public void AddFilter(string filterString,
                              bool bSelectNewFilter = false)
        {
            this.Filters.AddFilter(filterString, bSelectNewFilter);
        }

        /// <summary>
        /// Add a new filter argument to the list of filters and
        /// select this filter if <paramref name="bSelectNewFilter"/>
        /// indicates it.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="filterString"></param>
        /// <param name="bSelectNewFilter"></param>
        public void AddFilter(string name, string filterString,
                              bool bSelectNewFilter = false)
        {
            this.Filters.AddFilter(name, filterString, bSelectNewFilter);
        }
        #endregion Change filter methods

        /// <summary>
        /// Master controller interface method to navigate all views
        /// to the folder indicated in <paramref name="folder"/>
        /// - updates all related viewmodels.
        /// </summary>
        /// <param name="itemPath"></param>
        /// <param name="requestor"</param>
        public void NavigateToFolder(IPathModel itemPath)
        {
            // XXX Todo Keep task reference, support cancel, and remove on end?
            var t = NavigateToFolderAsync(itemPath, null);
        }

        /// <summary>
        /// Method executes when the user requests via UI bound command
        /// to refresh the currently displayed list of items.
        /// </summary>
        /// <returns></returns>
        protected void RefreshCommand_Executed(string path = null)
        {
            try
            {
                IPathModel location = null;
                if (path != null)
                    location = PathFactory.Create(path);
                else
                    location = PathFactory.Create(FolderTextPath.CurrentFolder);

                // XXX Todo Keep task reference, support cancel, and remove on end?
                var t = NavigateToFolderAsync(location, null);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Executes when the file open event is fired and class was constructed with statndard constructor.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void FolderItemsView_OnFileOpen(object sender,
                                                  FileSystemModels.Events.FileOpenEventArgs e)
        {
            MessageBox.Show("File Open:" + e.FileName);
        }

        /// <summary>
        /// Applies the file/directory filter from the combobox on the listview entries.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FileViewFilter_Changed(object sender, FileSystemModels.Events.FilterChangedEventArgs e)
        {
            this.FolderItemsView.ApplyFilter(e.FilterText);
        }

        /// <summary>
        /// The list view of folders and files requests to add or remove a folder
        /// to/from the collection of recent folders.
        /// -> Forward event to <seealso cref="FolderComboBoxViewModel"/> who manages
        /// the actual list of recent folders.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FolderItemsView_RequestEditBookmarkedFolders(object sender, EditBookmarkEvent e)
        {
            switch (e.Action)
            {
                case EditBookmarkEvent.RecentFolderAction.Remove:
                    this.RecentFolders.RemoveFolder(e.Folder);
                    break;

                case EditBookmarkEvent.RecentFolderAction.Add:
                    this.RecentFolders.AddFolder(e.Folder.Path);
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// Master controler interface method to navigate all views
        /// to the folder indicated in <paramref name="folder"/>
        /// - updates all related viewmodels.
        /// </summary>
        /// <param name="itemPath"></param>
        /// <param name="requestor"</param>
        private async Task NavigateToFolderAsync(IPathModel itemPath, object sender)
        {
            // Make sure the task always processes the last input but is not started twice
            await SlowStuffSemaphore.WaitAsync();
            try
            {
                TreeBrowser.SetExternalBrowsingState(true);
                FolderItemsView.SetExternalBrowsingState(true);
                FolderTextPath.SetExternalBrowsingState(true);

                bool? browseResult = null;

                // Navigate TreeView to this file system location
                if (TreeBrowser != sender)
                    browseResult = await TreeBrowser.NavigateToAsync(itemPath);

                // Navigate Folder ComboBox to this folder
                if (FolderTextPath != sender && browseResult != false)
                    browseResult = await FolderTextPath.NavigateToAsync(itemPath);

                // Navigate Folder/File ListView to this folder
                if (FolderItemsView != sender && browseResult != false)
                    browseResult = await FolderItemsView.NavigateToAsync(itemPath);

                if (browseResult == true)
                    SelectedFolder = itemPath.Path;
            }
            catch { }
            finally
            {
                TreeBrowser.SetExternalBrowsingState(true);
                FolderItemsView.SetExternalBrowsingState(false);
                FolderTextPath.SetExternalBrowsingState(false);

                SlowStuffSemaphore.Release();
            }
        }

        private void FolderTextPath_BrowseEvent(object sender,
                                                FileSystemModels.Browse.BrowsingEventArgs e)
        {
            var location = e.Location;

            SelectedFolder = location.Path;

            if (e.IsBrowsing == false && e.Result == BrowseResult.Complete)
            {
                // XXX Todo Keep task reference, support cancel, and remove on end?
                var t = NavigateToFolderAsync(location, sender);
            }
            else
            {
                if (e.IsBrowsing == true)
                {
                    // The sender has messaged: "I am chnaging location..."
                    // So, we set this property to tell the others:
                    // 1) Don't change your location now (eg.: Disable UI)
                    // 2) We'll be back to tell you the location when we know it
                    if (TreeBrowser != sender)
                        TreeBrowser.SetExternalBrowsingState(true);

                    if (FolderTextPath != sender)
                        FolderTextPath.SetExternalBrowsingState(true);

                    if (FolderItemsView != sender)
                        FolderItemsView.SetExternalBrowsingState(true);
                }
            }
        }
        #endregion methods
    }
}
