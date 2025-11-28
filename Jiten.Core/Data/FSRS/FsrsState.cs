namespace Jiten.Core.Data.FSRS;

/// <summary>
/// Represents the current learning state of a card
/// </summary>
public enum FsrsState
{
    /// <summary>
    /// No state or state resetted
    /// </summary>
    New = 0,
    
    /// <summary>
    /// Card is being learned for the first time
    /// </summary>
    Learning = 1,

    /// <summary>
    /// Card has graduated to regular review schedule
    /// </summary>
    Review = 2,

    /// <summary>
    /// Card was forgotten and needs to be relearned
    /// </summary>
    Relearning = 3,
    
    /// <summary>
    /// Card is blacklisted and will never be reviewed
    /// </summary>
    Blacklisted = 4,
    
    /// <summary>
    /// Card marked as always known, will never be reviewed
    /// </summary>
    Mastered = 5
}