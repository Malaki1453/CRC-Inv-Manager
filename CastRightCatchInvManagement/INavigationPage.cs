namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Nested workspace page. Called when the page is shown or when data/view mode changes.
    /// </summary>
    public interface INavigationPage
    {
        /// <summary>Reload the page so it matches the current database view and data.</summary>
        void HighlightCurrentPage();
    }
}