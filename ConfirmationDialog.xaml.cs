using System.Windows;

namespace Klavrio;

public partial class ConfirmationDialog : Window
{
    internal ConfirmationDialog(
        Window owner,
        string title,
        string question,
        string details,
        string confirmText,
        string cancelText,
        bool rightToLeft)
    {
        InitializeComponent();
        Owner = owner;
        Title = title;
        DialogTitle.Text = title;
        DialogQuestion.Text = question;
        DialogDetails.Text = details;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;

        System.Windows.FlowDirection direction = rightToLeft
            ? System.Windows.FlowDirection.RightToLeft
            : System.Windows.FlowDirection.LeftToRight;
        FlowDirection = direction;
        ConfirmButton.FlowDirection = direction;
        CancelButton.FlowDirection = direction;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
