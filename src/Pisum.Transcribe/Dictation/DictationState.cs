namespace Pisum.Transcribe.Dictation;

/// <summary>
/// The <see cref="IDictationState"/> that <see cref="DictationController"/> sets (design D5 of add-macos-dictation).
/// </summary>
internal sealed class DictationState : IDictationState
{
    private volatile bool _isActive;

    /// <inheritdoc />
    public event EventHandler? ActiveChanged;

    /// <inheritdoc />
    public bool IsActive => _isActive;

    /// <summary>
    /// Sets <see cref="IsActive"/> and raises <see cref="ActiveChanged"/> when the value changes.
    /// </summary>
    /// <param name="isActive">Whether a dictation is in progress.</param>
    public void SetActive(bool isActive)
    {
        if (_isActive == isActive)
        {
            return;
        }

        _isActive = isActive;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }
}
