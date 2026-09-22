using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Pisum.Transcribe.Hosting;
using Pisum.Transcribe.Recording;
using Pisum.Transcribe.Settings;
using Pisum.Transcribe.SpeechModels;
using Pisum.Transcribe.Transcription;

namespace Pisum.Transcribe.SettingsWindow;

/// <summary>
/// The dictation section of the settings window: the push-to-talk hotkey, the task, the languages and the silence
/// trimming. Use it on the UI thread.
/// </summary>
/// <remarks>
/// While a new hotkey is recorded, push-to-talk is suspended and its raw key events feed a
/// <see cref="HotkeyRecorder"/>. Every way the recording ends resumes push-to-talk.
/// </remarks>
internal sealed partial class DictationSectionViewModel : ObservableObject
{
    private readonly IPushToTalkHotkey _hotkey;
    private readonly IUiDispatcher _uiDispatcher;
    private SpeechModel _model;
    private HotkeyRecorder? _recorder;

    // Set while several properties change together, so the languages are checked once afterwards.
    private bool _isUpdating;

    /// <summary>
    /// Initializes a new instance with the saved settings.
    /// </summary>
    /// <param name="settings">The saved settings.</param>
    /// <param name="model">The model selected in the window, whose languages the pickers offer.</param>
    /// <param name="hotkey">The push-to-talk hotkey, suspended while a new hotkey is recorded.</param>
    /// <param name="uiDispatcher">Reaches the UI thread.</param>
    public DictationSectionViewModel(AppSettings settings,
                                     SpeechModel model,
                                     IPushToTalkHotkey hotkey,
                                     IUiDispatcher uiDispatcher)
    {
        _hotkey = hotkey;
        _uiDispatcher = uiDispatcher;
        _model = model;

        _isUpdating = true;
        Task = settings.Transcription.Task;
        SourceLanguage = settings.Transcription.SourceLanguage;
        TargetLanguage = settings.Transcription.TargetLanguage;
        Hotkey = settings.Recording.Hotkey;
        TrimSilence = settings.VoiceActivity.Enabled;
        SourceOptions = [];
        TargetOptions = [];
        _isUpdating = false;
        UpdateLanguages();
    }

    /// <summary>
    /// The model selected in the window. The language pickers offer its languages.
    /// </summary>
    public SpeechModel Model
    {
        get => _model;
        set
        {
            if (SetProperty(ref _model, value))
            {
                UpdateLanguages();
            }
        }
    }

    /// <summary>
    /// Whether speech is translated or transcribed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTranslate), nameof(IsTranscribe))]
    public partial TranscriptionTask Task { get; set; }

    /// <summary>
    /// Whether the task is <see cref="TranscriptionTask.Translate"/>, which shows the target picker. For a radio button.
    /// </summary>
    public bool IsTranslate
    {
        get => Task == TranscriptionTask.Translate;
        set
        {
            if (value)
            {
                Task = TranscriptionTask.Translate;
            }
        }
    }

    /// <summary>
    /// Whether the task is <see cref="TranscriptionTask.Transcribe"/>, for a radio button.
    /// </summary>
    public bool IsTranscribe
    {
        get => Task == TranscriptionTask.Transcribe;
        set
        {
            if (value)
            {
                Task = TranscriptionTask.Transcribe;
            }
        }
    }

    /// <summary>
    /// The spoken language as an ISO 639-1 code.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSource))]
    public partial string SourceLanguage { get; set; }

    /// <summary>
    /// The output language of <see cref="TranscriptionTask.Translate"/> as an ISO 639-1 code.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTarget))]
    public partial string TargetLanguage { get; set; }

    /// <summary>
    /// The source languages the picker offers.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSource))]
    public partial IReadOnlyList<LanguageOption> SourceOptions { get; private set; }

    /// <summary>
    /// The target languages the picker offers. Empty for <see cref="TranscriptionTask.Transcribe"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTarget))]
    public partial IReadOnlyList<LanguageOption> TargetOptions { get; private set; }

    /// <summary>
    /// The source language as the picker selects it, or <see langword="null"/> if the model does not offer it.
    /// Setting <see langword="null"/> is ignored: a picker clears its selection when its options change, and the
    /// unsupported language must stay, so the error names it.
    /// </summary>
    public LanguageOption? SelectedSource
    {
        get => SourceOptions.FirstOrDefault(option => option.Code == SourceLanguage);
        set
        {
            if (value is not null)
            {
                SourceLanguage = value.Code;
            }
        }
    }

    /// <summary>
    /// The target language as the picker selects it, or <see langword="null"/> if it is not offered. Setting
    /// <see langword="null"/> is ignored, as for <see cref="SelectedSource"/>.
    /// </summary>
    public LanguageOption? SelectedTarget
    {
        get => TargetOptions.FirstOrDefault(option => option.Code == TargetLanguage);
        set
        {
            if (value is not null)
            {
                TargetLanguage = value.Code;
            }
        }
    }

    /// <summary>
    /// Why the model does not support the source language, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial string? SourceLanguageError { get; private set; }

    /// <summary>
    /// Why the model does not support the translation to the target language, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    public partial string? TargetLanguageError { get; private set; }

    /// <summary>
    /// Whether a validation error is shown.
    /// </summary>
    public bool HasErrors => SourceLanguageError is not null || TargetLanguageError is not null;

    /// <summary>
    /// The push-to-talk hotkey as key names, as saved.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyName))]
    public partial IReadOnlyList<string> Hotkey { get; private set; }

    /// <summary>
    /// The push-to-talk hotkey for display, such as <c>Right Ctrl</c>.
    /// </summary>
    public string HotkeyName => HotkeyText.Format(HotkeyParser.Parse(Hotkey, NullLogger.Instance));

    /// <summary>
    /// Why the last recorded hotkey was rejected, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? HotkeyError { get; private set; }

    /// <summary>
    /// Whether a new hotkey is being recorded.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeHotkeyCommand))]
    public partial bool IsRecordingHotkey { get; private set; }

    /// <summary>
    /// Whether the silence around the speech is trimmed before transcription, and recordings without speech are not
    /// transcribed.
    /// </summary>
    [ObservableProperty]
    public partial bool TrimSilence { get; set; }

    /// <summary>
    /// Ends a hotkey recording without a change, for example when the window loses focus or closes. Has no effect
    /// while no hotkey is recorded.
    /// </summary>
    public void CancelHotkeyRecording()
    {
        if (_recorder is null)
        {
            return;
        }

        _recorder.Cancel();
        EndHotkeyRecording();
    }

    /// <summary>
    /// Takes newly saved values for the settings the user has not edited.
    /// </summary>
    /// <param name="previous">The saved settings the section was based on.</param>
    /// <param name="current">The newly saved settings.</param>
    public void Rebase(AppSettings previous, AppSettings current)
    {
        _isUpdating = true;
        var before = previous.Transcription;
        var after = current.Transcription;
        Task = DraftValue.Rebase(Task, before.Task, after.Task);
        SourceLanguage = DraftValue.Rebase(SourceLanguage, before.SourceLanguage, after.SourceLanguage);
        TargetLanguage = DraftValue.Rebase(TargetLanguage, before.TargetLanguage, after.TargetLanguage);
        if (Hotkey.SequenceEqual(previous.Recording.Hotkey))
        {
            Hotkey = current.Recording.Hotkey;
        }

        TrimSilence = DraftValue.Rebase(TrimSilence, previous.VoiceActivity.Enabled, current.VoiceActivity.Enabled);
        _isUpdating = false;
        UpdateLanguages();
    }

    [RelayCommand(CanExecute = nameof(CanChangeHotkey))]
    private void ChangeHotkey()
    {
        _recorder = new HotkeyRecorder();
        HotkeyError = null;
        IsRecordingHotkey = true;

        // Subscribed first, so no key between the two calls is lost.
        _hotkey.RawKey += OnRawKey;
        _hotkey.Suspend();
    }

    private bool CanChangeHotkey()
    {
        return !IsRecordingHotkey;
    }

    private void OnRawKey(object? sender, RawKeyEventArgs e)
    {
        _ = _uiDispatcher.InvokeAsync(() => RecordKey(e.Key, e.IsPressed));
    }

    private void RecordKey(SharpHook.Data.KeyCode key, bool isPressed)
    {
        if (_recorder is null)
        {
            return;
        }

        switch (_recorder.OnKey(key, isPressed))
        {
            case HotkeyRecordingState.Captured:
                Hotkey = HotkeyText.Order(_recorder.Keys).Select(keyCode => keyCode.ToString()).ToList();
                EndHotkeyRecording();
                break;
            case HotkeyRecordingState.Rejected:
                HotkeyError = HotkeyRecorder.RejectedMessage;
                EndHotkeyRecording();
                break;
            case HotkeyRecordingState.Cancelled:
                EndHotkeyRecording();
                break;
        }
    }

    private void EndHotkeyRecording()
    {
        _hotkey.RawKey -= OnRawKey;
        _hotkey.Resume();
        _recorder = null;
        IsRecordingHotkey = false;
    }

    partial void OnTaskChanged(TranscriptionTask value)
    {
        UpdateLanguages();
    }

    partial void OnSourceLanguageChanged(string value)
    {
        UpdateLanguages();
    }

    partial void OnTargetLanguageChanged(string value)
    {
        UpdateLanguages();
    }

    private void UpdateLanguages()
    {
        if (_isUpdating)
        {
            return;
        }

        _isUpdating = true;
        var options = LanguageOptions.For(Model, Task, SourceLanguage);
        SourceOptions = options.Sources;
        TargetOptions = options.Targets;

        // A single choice, such as English for a German source, is taken without asking.
        if (options.Targets is [var onlyTarget] && TargetLanguage != onlyTarget.Code)
        {
            TargetLanguage = onlyTarget.Code;
        }

        _isUpdating = false;

        // The source is checked alone, so an error names the setting that the model does not support.
        SourceLanguageError = TranscriptionOptionsValidator.Validate(Model,
            new TranscriptionOptions(TranscriptionTask.Transcribe, SourceLanguage, SourceLanguage));
        TargetLanguageError = SourceLanguageError is null && Task == TranscriptionTask.Translate
            ? TranscriptionOptionsValidator.Validate(Model,
                new TranscriptionOptions(Task, SourceLanguage, TargetLanguage))
            : null;
    }
}
