using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Pisum.Transcribe.Permissions;

/// <summary>
/// One permission row of the setup window: its state and the button that asks for it. Use it on the UI thread.
/// </summary>
internal sealed partial class PermissionRowViewModel : ObservableObject
{
    private readonly Func<PermissionState, string?> _actionText;
    private readonly Func<Task> _onAction;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="permission">The permission.</param>
    /// <param name="name">The permission's name.</param>
    /// <param name="description">What Pisum Transcribe needs it for.</param>
    /// <param name="isRequired">Whether the setup is complete only with the permission.</param>
    /// <param name="actionText">The button text in a state, or <see langword="null"/> for no button.</param>
    /// <param name="onAction">Runs when the user chooses the button.</param>
    public PermissionRowViewModel(Permission permission,
                                  string name,
                                  string description,
                                  bool isRequired,
                                  Func<PermissionState, string?> actionText,
                                  Func<Task> onAction)
    {
        Permission = permission;
        Name = name;
        Description = description;
        IsRequired = isRequired;
        _actionText = actionText;
        _onAction = onAction;
    }

    /// <summary>
    /// The permission.
    /// </summary>
    public Permission Permission { get; }

    /// <summary>
    /// The permission's name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// What Pisum Transcribe needs the permission for.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Whether the setup is complete only with the permission.
    /// </summary>
    public bool IsRequired { get; }

    /// <summary>
    /// <c>Required</c> or <c>Optional</c>.
    /// </summary>
    public string RequiredText => IsRequired ? "Required" : "Optional";

    /// <summary>
    /// The state.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(ActionText), nameof(HasAction))]
    [NotifyCanExecuteChangedFor(nameof(ActionCommand))]
    public partial PermissionState State { get; set; }

    /// <summary>
    /// A note below the state, such as that the application restarts, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string? Note { get; set; }

    /// <summary>
    /// Whether there is a <see cref="Note"/>.
    /// </summary>
    public bool HasNote => Note is not null;

    /// <summary>
    /// The state as text.
    /// </summary>
    public string StatusText => State switch
    {
        PermissionState.Granted => "Allowed",
        PermissionState.Denied => "Denied",
        PermissionState.AsksEachTime => "Asks each time",
        _ => "Not allowed yet",
    };

    /// <summary>
    /// The button text, or <see langword="null"/> when the row has no button in its state.
    /// </summary>
    public string? ActionText => _actionText(State);

    /// <summary>
    /// Whether the row has a button in its state.
    /// </summary>
    public bool HasAction => ActionText is not null;

    [RelayCommand(CanExecute = nameof(HasAction))]
    private Task ActionAsync()
    {
        return _onAction();
    }
}
