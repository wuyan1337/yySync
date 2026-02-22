#nullable disable
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace MusicRpc;
internal sealed class SteamLoginForm : Form
{
    private TextBox _txtUser;
    private TextBox _txtPass;
    private CheckBox _chkRemember;
    private Button _btnLogin;
    private Button _btnCancel;
    private Label _lblStatus;
    private readonly SteamSessionManager _session;
    private bool _isLoginInProgress;
    public bool LoginSucceeded { get; private set; }
    public SteamLoginForm(SteamSessionManager session)
    {
        _session = session;
        InitializeComponent();
        Load += async (s, e) => await AttemptAutoLogin();
    }
    private void InitializeComponent()
    {
        Text = "Steam Login";
        Size = new Size(350, 280);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        var lblUser = new Label { Text = "Steam Username:", Location = new Point(20, 20), AutoSize = true };
        _txtUser = new TextBox { Location = new Point(20, 45), Width = 290 };
        if (!string.IsNullOrEmpty(Configurations.Instance.Settings.SteamUsername))
        {
             _txtUser.Text = Configurations.Instance.Settings.SteamUsername;
        }
        var lblPass = new Label { Text = "Password:", Location = new Point(20, 80), AutoSize = true };
        _txtPass = new TextBox { Location = new Point(20, 105), Width = 290, UseSystemPasswordChar = true };
        _chkRemember = new CheckBox 
        { 
            Text = "Remember me (Auto Login)", 
            Location = new Point(20, 140), 
            AutoSize = true,
            Checked = true 
        };
        _btnLogin = new Button 
        { 
            Text = "Login", 
            Location = new Point(130, 180), 
            Width = 80, 
            Height = 30,
            DialogResult = DialogResult.None
        };
        _btnLogin.Click += async (s, e) => await PerformLogin();
        _btnCancel = new Button 
        { 
            Text = "Cancel", 
            Location = new Point(230, 180), 
            Width = 80, 
            Height = 30,
            DialogResult = DialogResult.Cancel 
        };
        _lblStatus = new Label 
        { 
            Text = "", 
            Location = new Point(20, 220), 
            Width = 300, 
            ForeColor = Color.Red 
        };
        Controls.AddRange(new Control[] { lblUser, _txtUser, lblPass, _txtPass, _chkRemember, _btnLogin, _btnCancel, _lblStatus });
        AcceptButton = _btnLogin;
        CancelButton = _btnCancel;
    }
    private async Task AttemptAutoLogin()
    {
        var savedUser = Configurations.Instance.Settings.SteamUsername;
        var savedToken = Configurations.Instance.Settings.SteamRefreshToken;
        if (!string.IsNullOrEmpty(savedUser) && !string.IsNullOrEmpty(savedToken))
        {
            _isLoginInProgress = true;
            UpdateUiState();
            _lblStatus.ForeColor = Color.Blue;
            _lblStatus.Text = "Auto-logging in...";
            bool success = await Task.Run(() => _session.LoginWithTokenAsync(savedUser, savedToken));
            if (success)
            {
                LoginSucceeded = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _lblStatus.ForeColor = Color.Red;
                _lblStatus.Text = "Session expired. Please login again.";
                _isLoginInProgress = false;
                UpdateUiState();
            }
        }
    }
    private async Task PerformLogin()
    {
        if (_isLoginInProgress) return;
        var user = _txtUser.Text.Trim();
        var pass = _txtPass.Text.Trim();
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            ShowStatus("Username and Password required.", Color.Red);
            return;
        }
        _isLoginInProgress = true;
        UpdateUiState();
        ShowStatus("Logging in...", Color.Blue);
        _session.OnSteamGuardRequired += (isMobile) =>
        {
             Invoke(() => 
             {
                 if (isMobile)
                 {
                     ShowStatus("Steam Guard Mobile: Confirm on your phone!", Color.Blue);
                 }
                 else
                 {
                     string code = InputBox("Steam Guard Required", "Enter Email Code:", isMobile);
                     if (!string.IsNullOrEmpty(code))
                     {
                         _session.SubmitSteamGuardCode(code);
                         ShowStatus("Verifying code...", Color.Blue);
                     }
                     else
                     {
                         _session.SubmitSteamGuardCode(""); 
                         ShowStatus("Cancelled.", Color.Red);
                     }
                 }
             });
        };
        bool success = await Task.Run(() => _session.LoginAsync(user, pass));
        if (success)
        {
            LoginSucceeded = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            ShowStatus(_session.LoginError ?? "Login Failed", Color.Red);
            _isLoginInProgress = false;
            UpdateUiState();
        }
    }
    private void ShowStatus(string msg, Color color)
    {
        if (InvokeRequired) Invoke(() => ShowStatus(msg, color));
        else
        {
            _lblStatus.ForeColor = color;
            _lblStatus.Text = msg;
        }
    }
    private void UpdateUiState()
    {
        _txtUser.Enabled = !_isLoginInProgress;
        _txtPass.Enabled = !_isLoginInProgress;
        _btnLogin.Enabled = !_isLoginInProgress;
        _chkRemember.Enabled = !_isLoginInProgress;
        _btnLogin.Text = _isLoginInProgress ? "..." : "Login";
        Cursor = _isLoginInProgress ? Cursors.WaitCursor : Cursors.Default;
    }
    private string InputBox(string title, string prompt, bool isMobile)
    {
        Form promptForm = new Form()
        {
            Width = 330,
            Height = 160,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false
        };
        Label textLabel = new Label() { Left = 20, Top = 20, Text = prompt, AutoSize = true };
        TextBox textBox = new TextBox() { Left = 20, Top = 50, Width = 270 };
        Button confirmation = new Button() { Text = "OK", Left = 210, Width = 80, Top = 85, DialogResult = DialogResult.OK };
        promptForm.Controls.AddRange(new Control[] { textLabel, textBox, confirmation });
        promptForm.AcceptButton = confirmation;
        return promptForm.ShowDialog() == DialogResult.OK ? textBox.Text : null;
    }
}