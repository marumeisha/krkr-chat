using System.Globalization;

namespace SecureChat.Client.Modules.OnlineUsers;

public sealed class OnlineUsersPanel : UserControl
{
    private readonly GroupBox _groupBox = new() { Text = "Online Users", Dock = DockStyle.Fill };
    private readonly Panel _toolbarPanel = new() { Dock = DockStyle.Top, Height = 30 };
    private readonly CheckBox _onlineOnlyCheckBox = new()
    {
        Text = "Only Online",
        AutoSize = true,
        Left = 8,
        Top = 6
    };
    private readonly ListView _listView = new()
    {
        Dock = DockStyle.Fill,
        FullRowSelect = true,
        View = View.Details,
        MultiSelect = false,
        GridLines = true
    };
    private readonly Label _hintLabel = new()
    {
        Dock = DockStyle.Bottom,
        AutoSize = false,
        Height = 32,
        Text = "Server-side status: heartbeat within last 2 minutes.",
        TextAlign = ContentAlignment.MiddleLeft
    };
    private IReadOnlyList<OnlineUserEntry> _entries = [];
    private string _currentUserId = "";

    public event Action<string>? UserPicked;

    public OnlineUsersPanel()
    {
        _listView.Columns.Add("User ID", 180);
        _listView.Columns.Add("Status", 70);
        _listView.Columns.Add("Last Seen", 120);

        _onlineOnlyCheckBox.CheckedChanged += (_, _) => RefreshRows();

        _listView.DoubleClick += (_, _) => OnUserPicked();
        _listView.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                OnUserPicked();
                e.Handled = true;
            }
        };

        _toolbarPanel.Controls.Add(_onlineOnlyCheckBox);
        _groupBox.Controls.Add(_toolbarPanel);
        _groupBox.Controls.Add(_listView);
        _groupBox.Controls.Add(_hintLabel);
        Controls.Add(_groupBox);
    }

    public void SetEntries(IReadOnlyList<OnlineUserEntry> entries, string? currentUserId = null)
    {
        _entries = entries;
        _currentUserId = currentUserId ?? "";
        RefreshRows();
    }

    private void RefreshRows()
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();

        foreach (var entry in _entries)
        {
            if (_onlineOnlyCheckBox.Checked && !entry.IsOnline)
            {
                continue;
            }

            var displayUserId = string.Equals(entry.UserId, _currentUserId, StringComparison.OrdinalIgnoreCase)
                ? $"{entry.UserId} (me)"
                : entry.UserId;

            var row = new ListViewItem(displayUserId)
            {
                Tag = entry.UserId
            };
            row.SubItems.Add(entry.IsOnline ? "Online" : "Offline");
            row.SubItems.Add(entry.LastSeenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            if (entry.IsOnline)
            {
                row.ForeColor = Color.DarkGreen;
            }

            _listView.Items.Add(row);
        }

        _listView.EndUpdate();
    }

    private void OnUserPicked()
    {
        if (_listView.SelectedItems.Count == 0)
        {
            return;
        }

        var selected = _listView.SelectedItems[0].Tag as string;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            UserPicked?.Invoke(selected);
        }
    }
}
