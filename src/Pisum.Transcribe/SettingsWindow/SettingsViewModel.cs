using System.ComponentModel;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// The view model of the settings window. The sections edit a draft of the saved settings; <b>Save</b> applies it and
/// keeps the window open, and closing the window discards it. Use it on the UI thread.
/// </summary>
/// <remarks>
/// Settings saved elsewhere while the window is open, such as the model chosen in the setup window, replace the values
/// the user has not edited. Save therefore writes exactly what the window shows and never an old value of a setting
/// that the user did not touch. "Start with Windows" is not a setting; it is read from and written to Windows.
/// </remarks>
internal sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>
    /// The message when the settings file could not be written.
    /// </summary>
    public const string SaveFailedMessage = "The settings could not be saved. Details are in the log.";

    /// <summary>
    /// The message when "Start with Windows" could not be changed.
    /// </summary>
    public const string StartupFailedMessage = "Start with Windows could not be changed. Details are in the log.";

    private readonly ISettingsStore _settingsStore;
    private readonly IStartupRegistration _startupRegistration;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly Action<Action> _invokeOnUiThread;

    // The saved state the draft is based on.
    private AppSettings _baseline;
    private bool _startsWithWindows;
    private bool _isSaving;

    /// <summary>
    /// Initializes a new instance with the saved settings.
    /// </summary>
    /// <param name="settingsStore">The settings store.</param>
    /// <param name="startupRegistration">Starts the application at sign-in.</param>
    /// <param name="modelStore">The model store.</param>
    /// <param name="transcriber">The transcription engine.</param>
    /// <param name="hotkey">The push-to-talk hotkey, suspended while a new hotkey is recorded.</param>
    /// <param name="lifetime">The application lifetime.</param>
    /// <param name="confirmDelete">Asks the user whether to delete a model.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="invokeOnUiThread">
    /// Queues an action on the UI thread, for tests. <see langword="null"/> uses the WPF dispatcher.
    /// </param>
    public SettingsViewModel(ISettingsStore settingsStore,
                             IStartupRegistration startupRegistration,
                             IModelStore modelStore,
                             ITranscriber transcriber,
                             IPushToTalkHotkey hotkey,
                             IHostApplicationLifetime lifetime,
                             Func<SpeechModel, bool> confirmDelete,
                             ILogger<SettingsViewModel> logger,
                             Action<Action>? invokeOnUiThread = null)
    {
        _settingsStore = settingsStore;
        _startupRegistration = startupRegistration;
        _logger = logger;
        _invokeOnUiThread = invokeOnUiThread ?? (action => Application.Current.Dispatcher.InvokeAsync(action));
        _baseline = settingsStore.Current;
        _startsWithWindows = startupRegistration.IsEnabled();

        Model = new ModelSectionViewModel(_baseline, modelStore, transcriber, lifetime.ApplicationStopping,
            confirmDelete, _invokeOnUiThread);
        Dictation = new DictationSectionViewModel(_baseline, Model.SelectedModel, hotkey, _invokeOnUiThread);
        TextInsertion = new TextInsertionSectionViewModel(_baseline.TextInsertion);
        General = new GeneralSectionViewModel(_startsWithWindows, _baseline.Updates);

        Model.PropertyChanged += OnSectionChanged;
        Dictation.PropertyChanged += OnSectionChanged;
        TextInsertion.PropertyChanged += OnSectionChanged;
        General.PropertyChanged += OnSectionChanged;
        _settingsStore.Changed += OnSettingsChanged;
    }

    /// <summary>
    /// The dictation section.
    /// </summary>
    public DictationSectionViewModel Dictation { get; }

    /// <summary>
    /// The model section.
    /// </summary>
    public ModelSectionViewModel Model { get; }

    /// <summary>
    /// The text insertion section.
    /// </summary>
    public TextInsertionSectionViewModel TextInsertion { get; }

    /// <summary>
    /// The general section.
    /// </summary>
    public GeneralSectionViewModel General { get; }

    /// <summary>
    /// Whether the window shows edits that are not saved.
    /// </summary>
    public bool HasChanges => BuildSettings() != _baseline || General.StartWithWindows != _startsWithWindows;

    /// <summary>
    /// Why the last save failed, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? SaveError { get; private set; }

    /// <summary>
    /// Decides whether the window may close. Unsaved edits are discarded without asking; only a running download
    /// asks first.
    /// </summary>
    /// <param name="confirmCancelDownload">Asks the user whether to cancel the download and close.</param>
    /// <returns><see langword="true"/> if the window may close.</returns>
    public bool ConfirmClose(Func<bool> confirmCancelDownload)
    {
        return Model.ConfirmClose(confirmCancelDownload);
    }

    /// <summary>
    /// Ends a hotkey recording and stops following the settings, when the window has closed.
    /// </summary>
    public void OnClosed()
    {
        Dictation.CancelHotkeyRecording();
        Model.Detach();
        _settingsStore.Changed -= OnSettingsChanged;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        _isSaving = true;
        SaveCommand.NotifyCanExecuteChanged();
        SaveError = null;
        try
        {
            var settings = BuildSettings();
            if (settings != _baseline)
            {
                try
                {
                    await _settingsStore.SaveAsync(settings, CancellationToken.None);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _logger.LogError(exception, "Saving the settings failed");
                    SaveError = SaveFailedMessage;
                    return;
                }

                ApplyBaseline(settings);
            }

            if (General.StartWithWindows != _startsWithWindows)
            {
                try
                {
                    _startupRegistration.SetEnabled(General.StartWithWindows);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                      or SecurityException)
                {
                    _logger.LogError(exception, "Changing Start with Windows failed");
                    SaveError = StartupFailedMessage;
                }

                _startsWithWindows = _startupRegistration.IsEnabled();
                General.StartWithWindows = _startsWithWindows;
            }
        }
        finally
        {
            _isSaving = false;
            NotifySaveState();
        }
    }

    private bool CanSave()
    {
        return !_isSaving && !Dictation.HasErrors && HasChanges;
    }

    private AppSettings BuildSettings()
    {
        return _baseline with
        {
            Model = _baseline.Model with {SelectedModelId = Model.SelectedModelId},
            Transcription = new TranscriptionSettings(Model.Backend, Dictation.Task, Dictation.SourceLanguage,
                Dictation.TargetLanguage),
            Recording = _baseline.Recording with {Hotkey = Dictation.Hotkey},
            TextInsertion = TextInsertion.ToSettings(),
            VoiceActivity = new VoiceActivitySettings(Dictation.TrimSilence),
            Updates = new UpdateSettings(General.CheckForUpdates),
        };
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        _invokeOnUiThread(() => ApplyBaseline(e.Current));
    }

    private void ApplyBaseline(AppSettings current)
    {
        if (ReferenceEquals(current, _baseline))
        {
            return;
        }

        var previous = _baseline;
        _baseline = current;
        Model.Rebase(previous, current);
        Dictation.Model = Model.SelectedModel;
        Dictation.Rebase(previous, current);
        TextInsertion.Rebase(previous.TextInsertion, current.TextInsertion);
        General.Rebase(previous.Updates, current.Updates);
        NotifySaveState();
    }

    private void OnSectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == Model && e.PropertyName == nameof(ModelSectionViewModel.SelectedModelId))
        {
            Dictation.Model = Model.SelectedModel;
        }

        NotifySaveState();
    }

    private void NotifySaveState()
    {
        OnPropertyChanged(nameof(HasChanges));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
