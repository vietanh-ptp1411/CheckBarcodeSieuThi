using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CheckBarcodeSieuThi.Services;

namespace CheckBarcodeSieuThi.ViewModels
{
    /// <summary>Một lựa chọn của tham số dạng danh sách: giá trị gửi xuống đầu đọc và nhãn hiển thị.</summary>
    public sealed record ConfigOption(string Value, string Label);

    /// <summary>
    /// Cấu hình tham số bên trong đầu đọc (chế độ quét, loại mã, ký tự kết thúc, đèn, camera...)
    /// qua giao thức của phần mềm ShiJieConfig, dùng chính kết nối TCP đang nhận mã vạch.
    /// </summary>
    public sealed class ReaderConfigViewModel : ViewModelBase
    {
        // ----- Mã tham số (6 ký tự hex) trên dòng ICW76Pro / ICW7x -----
        private const string ScanModeCode = "0E0100";
        private const string TriggerTimeoutCode = "0E0105";
        private const string RereadDelayCode = "080B06";
        private const string TriDecodeTimeoutCode = "024D21";
        private const string TriDecodeTimeout2Code = "024D22";
        private const string ConDecodeTimeoutCode = "024E0D";
        private const string ConDecodeTimeout2Code = "024E0E";
        private const string MultiCodeCode = "0F0120";
        private const string TriggerCmdCode = "0F0184";
        private const string ReleaseCmdCode = "0F0185";
        private const string PrefixCode = "080400";
        private const string SuffixCode = "080500";
        private const string ReadFailPromptCode = "0F0140";
        private const string ReadFailTextCode = "025004";
        private const string IllumCode = "040905";
        private const string IllumLevelCode = "0F0145";
        private const string LightControlCode = "0F0123";
        private const string AimerCode = "040906";
        private const string BeepCode = "0F0141";
        private const string ConExpModeCode = "040211";
        private const string ConExposureCode = "040306";
        private const string ConGainCode = "040307";
        private const string TriExpModeCode = "040210";
        private const string TriExposureCode = "040217";
        private const string TriGainCode = "040216";
        private const string FocusCode = "0F028B";

        // Loại mã: mã lệnh bật/tắt (1/0). EAN và UPC gồm 2 lệnh (EAN-13/8, UPC-A/E).
        private static readonly (string Key, string[] Codes)[] SymbologyCodes =
        [
            ("EAN", ["021301", "021401"]),
            ("UPC", ["021101", "021201"]),
            ("Code128", ["020A01"]),
            ("Code39", ["020301"]),
            ("Code93", ["020D01"]),
            ("I25", ["020401"]),
            ("PDF417", ["021F01"]),
            ("QR", ["023701"]),
            ("DataMatrix", ["023601"]),
        ];

        private static readonly string[] ValueCodes =
        [
            ScanModeCode, TriggerTimeoutCode, RereadDelayCode, TriDecodeTimeoutCode, ConDecodeTimeoutCode, MultiCodeCode,
            TriggerCmdCode, ReleaseCmdCode, PrefixCode, SuffixCode, ReadFailPromptCode, ReadFailTextCode,
            IllumCode, IllumLevelCode, LightControlCode, AimerCode, BeepCode,
            ConExpModeCode, ConExposureCode, ConGainCode, TriExpModeCode, TriExposureCode, TriGainCode, FocusCode,
            .. SymbologyCodes.SelectMany(s => s.Codes),
        ];

        private static readonly string[] RangeCodes =
        [
            ScanModeCode, TriggerTimeoutCode, RereadDelayCode, ConDecodeTimeoutCode, MultiCodeCode, IllumLevelCode, LightControlCode,
            AimerCode, BeepCode, ConExpModeCode, ConExposureCode, ConGainCode, TriExpModeCode, TriExposureCode, TriGainCode, FocusCode,
        ];

        private readonly BarcodeReaderService _reader;
        private readonly ReaderConfigService _service;
        private readonly Action<string> _log;

        /// <summary>Giá trị đọc được từ đầu đọc lần gần nhất (mã lệnh → giá trị), dùng để chỉ ghi những gì thay đổi.</summary>
        private Dictionary<string, string> _original = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _ranges = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Người dùng chọn một đầu đọc trong danh sách tìm thấy.</summary>
        public event Action<DiscoveredReader>? ReaderSelected;

        public ObservableCollection<DiscoveredReader> Found { get; } = [];

        public ICommand DiscoverCommand { get; }
        public ICommand ReadCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SupermarketPresetCommand { get; }
        public ICommand RestoreFactoryCommand { get; }
        public ICommand TriggerCommand { get; }
        public ICommand ReleaseCommand { get; }

        public ReaderConfigViewModel(BarcodeReaderService reader, Action<string> log)
        {
            _reader = reader;
            _service = new ReaderConfigService(reader);
            _log = log;

            DiscoverCommand = new RelayCommand(async () => await DiscoverAsync(), () => !IsBusy);
            ReadCommand = new RelayCommand(async () => await ReadAsync(), () => CanTalk);
            SaveCommand = new RelayCommand(async () => await SaveAsync(), () => CanTalk && HasConfig);
            SupermarketPresetCommand = new RelayCommand(ApplySupermarketPreset, () => HasConfig && !IsBusy);
            RestoreFactoryCommand = new RelayCommand(async () => await RestoreFactoryAsync(), () => CanTalk);
            TriggerCommand = new RelayCommand(async () => await TriggerAsync(true), () => CanTalk);
            ReleaseCommand = new RelayCommand(async () => await TriggerAsync(false), () => CanTalk);
        }

        private bool CanTalk => !IsBusy && _reader.IsRunning && _reader.ConnectedCount > 0;

        // ==================================================================
        // Trạng thái
        // ==================================================================

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }

        private bool _hasConfig;
        /// <summary>Đã đọc được cấu hình từ đầu đọc, các ô nhập có giá trị thật.</summary>
        public bool HasConfig { get => _hasConfig; private set => SetField(ref _hasConfig, value); }

        private string _status = "Chưa đọc cấu hình từ đầu đọc.";
        public string Status { get => _status; private set => SetField(ref _status, value); }

        private bool _statusIsError;
        public bool StatusIsError { get => _statusIsError; private set => SetField(ref _statusIsError, value); }

        private string _deviceSummary = "";
        /// <summary>Model · firmware · số serial.</summary>
        public string DeviceSummary { get => _deviceSummary; private set => SetField(ref _deviceSummary, value); }

        private void SetStatus(string text, bool error = false)
        {
            Status = text;
            StatusIsError = error;
            _log(text);
        }

        // ==================================================================
        // Chế độ quét
        // ==================================================================

        public ObservableCollection<ConfigOption> ScanModeOptions { get; } =
        [
            new("0", "Kích đọc (trigger): cảm biến, chân IN hoặc lệnh trigger"),
            new("8", "Đọc liên tục (tự phát hiện mã)"),
        ];

        private string _scanMode = "8";
        public string ScanMode { get => _scanMode; set => SetField(ref _scanMode, value); }

        private int _triggerTimeoutMs = 1000;
        /// <summary>Ở chế độ trigger: thời gian tối đa giữ đọc sau một lần trigger.</summary>
        public int TriggerTimeoutMs { get => _triggerTimeoutMs; set => SetField(ref _triggerTimeoutMs, value); }

        private int _rereadDelayMs = 750;
        /// <summary>Chống đọc lặp ngay trên đầu đọc: cùng một mã chỉ gửi lại sau khoảng này.</summary>
        public int RereadDelayMs { get => _rereadDelayMs; set => SetField(ref _rereadDelayMs, value); }

        private int _decodeTimeoutMs = 1454;
        public int DecodeTimeoutMs { get => _decodeTimeoutMs; set => SetField(ref _decodeTimeoutMs, value); }

        private int _multiCodeCount = 1;
        /// <summary>Số mã tối đa giải trong một lần đọc. Bán hàng nên để 1.</summary>
        public int MultiCodeCount { get => _multiCodeCount; set => SetField(ref _multiCodeCount, value); }

        private string _triggerCommandText = "TRIGGER";
        public string TriggerCommandText { get => _triggerCommandText; set => SetField(ref _triggerCommandText, value); }

        private string _releaseCommandText = "RELEASE";
        public string ReleaseCommandText { get => _releaseCommandText; set => SetField(ref _releaseCommandText, value); }

        // ==================================================================
        // Loại mã
        // ==================================================================

        private readonly Dictionary<string, bool> _symbologies = SymbologyCodes.ToDictionary(s => s.Key, _ => false);

        private bool GetSym(string key) => _symbologies[key];
        private void SetSym(string key, bool value, string propertyName)
        {
            if (_symbologies[key] == value) return;
            _symbologies[key] = value;
            OnPropertyChanged(propertyName);
        }

        public bool SymEan { get => GetSym("EAN"); set => SetSym("EAN", value, nameof(SymEan)); }
        public bool SymUpc { get => GetSym("UPC"); set => SetSym("UPC", value, nameof(SymUpc)); }
        public bool SymCode128 { get => GetSym("Code128"); set => SetSym("Code128", value, nameof(SymCode128)); }
        public bool SymCode39 { get => GetSym("Code39"); set => SetSym("Code39", value, nameof(SymCode39)); }
        public bool SymCode93 { get => GetSym("Code93"); set => SetSym("Code93", value, nameof(SymCode93)); }
        public bool SymI25 { get => GetSym("I25"); set => SetSym("I25", value, nameof(SymI25)); }
        public bool SymPdf417 { get => GetSym("PDF417"); set => SetSym("PDF417", value, nameof(SymPdf417)); }
        public bool SymQr { get => GetSym("QR"); set => SetSym("QR", value, nameof(SymQr)); }
        public bool SymDataMatrix { get => GetSym("DataMatrix"); set => SetSym("DataMatrix", value, nameof(SymDataMatrix)); }

        private void NotifyAllSymbologies()
        {
            foreach (var name in new[] { nameof(SymEan), nameof(SymUpc), nameof(SymCode128), nameof(SymCode39), nameof(SymCode93),
                                         nameof(SymI25), nameof(SymPdf417), nameof(SymQr), nameof(SymDataMatrix) })
                OnPropertyChanged(name);
        }

        // ==================================================================
        // Dữ liệu gửi về PC
        // ==================================================================

        private string _prefixText = "";
        /// <summary>Chuỗi thêm vào trước mã. Dạng hiển thị: [CR] [LF] [TAB] hoặc \xNN.</summary>
        public string PrefixText { get => _prefixText; set => SetField(ref _prefixText, value); }

        private string _suffixText = "[CR][LF]";
        /// <summary>Chuỗi thêm vào sau mã. Phần mềm này tách mã theo CR/LF nên nên để [CR][LF].</summary>
        public string SuffixText { get => _suffixText; set => SetField(ref _suffixText, value); }

        private bool _readFailPrompt;
        /// <summary>Gửi chuỗi báo lỗi khi kích đọc mà không đọc được mã.</summary>
        public bool ReadFailPrompt { get => _readFailPrompt; set => SetField(ref _readFailPrompt, value); }

        private string _readFailText = "NR";
        public string ReadFailText { get => _readFailText; set => SetField(ref _readFailText, value); }

        // ==================================================================
        // Đèn, ngắm, âm báo
        // ==================================================================

        private bool _illumOn = true;
        public bool IllumOn { get => _illumOn; set => SetField(ref _illumOn, value); }

        public ObservableCollection<ConfigOption> IllumLevelOptions { get; } = [];

        private string _illumLevel = "150";
        public string IllumLevel { get => _illumLevel; set => SetField(ref _illumLevel, value); }

        private bool _light1 = true, _light2, _light3 = true, _light4;
        public bool Light1 { get => _light1; set => SetField(ref _light1, value); }
        public bool Light2 { get => _light2; set => SetField(ref _light2, value); }
        public bool Light3 { get => _light3; set => SetField(ref _light3, value); }
        public bool Light4 { get => _light4; set => SetField(ref _light4, value); }

        public ObservableCollection<ConfigOption> AimerOptions { get; } = [];

        private string _aimer = "2";
        public string Aimer { get => _aimer; set => SetField(ref _aimer, value); }

        private int _beepTone = 100;
        public int BeepTone { get => _beepTone; set => SetField(ref _beepTone, value); }

        // ==================================================================
        // Camera
        // ==================================================================

        public ObservableCollection<ConfigOption> ExposureModeOptions { get; } = [];

        private string _conExposureMode = "0";
        public string ConExposureMode { get => _conExposureMode; set => SetField(ref _conExposureMode, value); }

        private int _conExposure = 3000;
        public int ConExposure { get => _conExposure; set => SetField(ref _conExposure, value); }

        private int _conGain = 15;
        public int ConGain { get => _conGain; set => SetField(ref _conGain, value); }

        private string _triExposureMode = "0";
        public string TriExposureMode { get => _triExposureMode; set => SetField(ref _triExposureMode, value); }

        private int _triExposure = 3000;
        public int TriExposure { get => _triExposure; set => SetField(ref _triExposure, value); }

        private int _triGain = 15;
        public int TriGain { get => _triGain; set => SetField(ref _triGain, value); }

        private int _focus = 284;
        public int Focus { get => _focus; set => SetField(ref _focus, value); }

        // Khoảng giá trị cho phép (hiện ở tooltip)
        public string TriggerTimeoutRange => RangeText(TriggerTimeoutCode, "ms");
        public string RereadDelayRange => RangeText(RereadDelayCode, "ms");
        public string DecodeTimeoutRange => RangeText(ConDecodeTimeoutCode, "ms");
        public string MultiCodeRange => RangeText(MultiCodeCode, "");
        public string BeepRange => RangeText(BeepCode, "");
        public string ExposureRange => RangeText(ConExposureCode, "");
        public string GainRange => RangeText(ConGainCode, "");
        public string FocusRange => RangeText(FocusCode, "");

        private string RangeText(string code, string unit)
        {
            var r = ShiYinProtocol.ParseRange(_ranges.GetValueOrDefault(code));
            return r == null ? "" : $"{r.Value.Min} – {r.Value.Max}{(unit.Length > 0 ? " " + unit : "")}";
        }

        private void NotifyRanges()
        {
            foreach (var name in new[] { nameof(TriggerTimeoutRange), nameof(RereadDelayRange), nameof(DecodeTimeoutRange), nameof(MultiCodeRange),
                                         nameof(BeepRange), nameof(ExposureRange), nameof(GainRange), nameof(FocusRange) })
                OnPropertyChanged(name);
        }

        // ==================================================================
        // Tìm thiết bị trong mạng
        // ==================================================================

        private DiscoveredReader? _selectedFound;
        public DiscoveredReader? SelectedFound
        {
            get => _selectedFound;
            set
            {
                if (!SetField(ref _selectedFound, value) || value == null) return;
                ReaderSelected?.Invoke(value);
            }
        }

        private async Task DiscoverAsync()
        {
            IsBusy = true;
            try
            {
                Found.Clear();
                SetStatus("Đang tìm đầu đọc trong mạng LAN (3 giây)...");
                var list = await ReaderDiscovery.SearchAsync(TimeSpan.FromSeconds(3), msg => OnUi(() => _log(msg)));
                foreach (var r in list) Found.Add(r);
                SetStatus(list.Count == 0
                    ? "Không tìm thấy đầu đọc nào. Kiểm tra đầu đọc đã cắm mạng, cùng dải IP với PC và firewall cho phép UDP."
                    : $"Tìm thấy {list.Count} đầu đọc. Bấm vào một dòng để điền IP và cổng.", list.Count == 0);
            }
            catch (Exception ex)
            {
                SetStatus($"Lỗi tìm thiết bị: {ex.Message}", true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ==================================================================
        // Đọc cấu hình
        // ==================================================================

        /// <summary>
        /// Gọi khi vừa kết nối: tự đọc thông tin và cấu hình, rồi bật đọc theo chế độ đã lưu
        /// (lúc thoát phần mềm đầu đọc đã bị tạm dừng bằng lệnh ghi tạm). Lỗi chỉ ghi log.
        /// </summary>
        public async Task ReadOnConnectedAsync()
        {
            await Task.Delay(400);
            if (!CanTalk) return;
            await ReadAsync();
            if (!HasConfig || !CanTalk) return;

            try
            {
                await _service.ApplyTemporaryAsync([ScanModeCode + ScanMode]);
                _log(ScanMode == "8" ? "Đầu đọc bắt đầu đọc liên tục." : "Đầu đọc ở chế độ kích đọc (trigger).");
            }
            catch (Exception ex)
            {
                _log($"Không bật lại chế độ đọc: {ex.Message}");
            }
        }

        /// <summary>
        /// Thoát phần mềm: nhả trigger và tạm chuyển đầu đọc sang chế độ trigger để nó ngừng đọc.
        /// Lệnh ghi tạm nên cấu hình đã lưu không đổi; lần mở phần mềm sau sẽ bật lại.
        /// Chạy trên thread nền, không được động vào giao diện.
        /// </summary>
        public async Task PauseForExitAsync()
        {
            if (!_reader.IsRunning || _reader.ConnectedCount == 0) return;
            try
            {
                await _service.ReleaseAsync();
                await _service.ApplyTemporaryAsync([ScanModeCode + "0"]);
            }
            catch (Exception)
            {
                // Đang thoát
            }
        }

        public async Task ReadAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                SetStatus("Đang đọc cấu hình từ đầu đọc...");
                var info = await _service.ReadDeviceInfoAsync();
                DeviceSummary = $"{info.Model} · firmware {info.Firmware} · SN {info.SerialNumber}";

                _ranges = await _service.ReadRangesAsync(RangeCodes);
                _original = await _service.ReadValuesAsync(ValueCodes);
                ApplyValuesToUi();
                HasConfig = true;
                SetStatus($"Đã đọc cấu hình lúc {DateTime.Now:HH:mm:ss} ({info.Model}, firmware {info.Firmware}).");
            }
            catch (Exception ex)
            {
                SetStatus($"Không đọc được cấu hình: {ex.Message}", true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private string Val(string code, string fallback = "") => _original.GetValueOrDefault(code, fallback);

        private int IntVal(string code, int fallback) => int.TryParse(Val(code), out var v) ? v : fallback;

        private void ApplyValuesToUi()
        {
            // Danh sách lựa chọn lấy theo khoảng giá trị đầu đọc báo về
            FillOptions(IllumLevelOptions, IllumLevelCode, v => v == "0" ? "Tắt" : $"{v}%");
            FillOptions(AimerOptions, AimerCode, v => v switch { "0" => "Tắt", "2" => "Bật khi đọc", "3" => "Luôn bật", _ => $"Kiểu {v}" });
            FillOptions(ExposureModeOptions, ConExpModeCode, v => v switch
            {
                "0" => "Cố định (đặt tay)",
                "4" => "Tự động",
                "2" => "Tự động – ưu tiên giữa khung",
                _ => $"Tự động – kiểu {v}",
            });

            ScanMode = EnsureOption(ScanModeOptions, Val(ScanModeCode, "8"), v => $"Chế độ {v}");
            TriggerTimeoutMs = IntVal(TriggerTimeoutCode, 1000);
            RereadDelayMs = IntVal(RereadDelayCode, 750);
            DecodeTimeoutMs = IntVal(ConDecodeTimeoutCode, IntVal(TriDecodeTimeoutCode, 1454));
            MultiCodeCount = IntVal(MultiCodeCode, 1);
            TriggerCommandText = ShiYinProtocol.HexToDisplay(Val(TriggerCmdCode));
            ReleaseCommandText = ShiYinProtocol.HexToDisplay(Val(ReleaseCmdCode));

            foreach (var (key, codes) in SymbologyCodes)
                _symbologies[key] = Val(codes[0]) == "1";
            NotifyAllSymbologies();

            PrefixText = AffixToDisplay(Val(PrefixCode));
            SuffixText = AffixToDisplay(Val(SuffixCode));
            ReadFailPrompt = Val(ReadFailPromptCode) == "1";
            ReadFailText = ShiYinProtocol.HexToDisplay(Val(ReadFailTextCode));

            IllumOn = Val(IllumCode) != "0";
            IllumLevel = EnsureOption(IllumLevelOptions, Val(IllumLevelCode, "150"), v => $"{v}%");
            var lights = IntVal(LightControlCode, 15);
            Light1 = (lights & 1) != 0;
            Light2 = (lights & 2) != 0;
            Light3 = (lights & 4) != 0;
            Light4 = (lights & 8) != 0;
            Aimer = EnsureOption(AimerOptions, Val(AimerCode, "2"), v => $"Kiểu {v}");
            BeepTone = IntVal(BeepCode, 100);

            ConExposureMode = EnsureOption(ExposureModeOptions, Val(ConExpModeCode, "0"), v => $"Kiểu {v}");
            ConExposure = IntVal(ConExposureCode, 3000);
            ConGain = IntVal(ConGainCode, 15);
            TriExposureMode = EnsureOption(ExposureModeOptions, Val(TriExpModeCode, "0"), v => $"Kiểu {v}");
            TriExposure = IntVal(TriExposureCode, 3000);
            TriGain = IntVal(TriGainCode, 15);
            Focus = IntVal(FocusCode, 284);

            NotifyRanges();
            _reader.NoReadText = ReadFailPrompt && ReadFailText.Length > 0 ? ReadFailText : null;
        }

        private void FillOptions(ObservableCollection<ConfigOption> target, string code, Func<string, string> label)
        {
            var values = ShiYinProtocol.ParseOptions(_ranges.GetValueOrDefault(code));
            if (values.Count == 0) return;
            target.Clear();
            foreach (var v in values)
                target.Add(new ConfigOption(v, label(v)));
        }

        /// <summary>Giá trị đọc được không nằm trong danh sách thì thêm vào để ComboBox vẫn hiện đúng.</summary>
        private static string EnsureOption(ObservableCollection<ConfigOption> options, string value, Func<string, string> label)
        {
            if (options.All(o => o.Value != value))
                options.Add(new ConfigOption(value, label(value)));
            return value;
        }

        /// <summary>Prefix/suffix lưu dạng "99" + hex ("99" = áp dụng cho mọi loại mã).</summary>
        private static string AffixToDisplay(string raw) =>
            raw.Length >= 2 ? ShiYinProtocol.HexToDisplay(raw[2..]) : "";

        private static string AffixToValue(string display) => "99" + ShiYinProtocol.DisplayToHex(display);

        // ==================================================================
        // Ghi cấu hình
        // ==================================================================

        /// <summary>Danh sách lệnh cho những tham số khác với giá trị đã đọc từ đầu đọc.</summary>
        private List<string> BuildChanges()
        {
            var cmds = new List<string>();

            void Set(string code, string value)
            {
                if (!_original.TryGetValue(code, out var old) || old != value)
                    cmds.Add(code + value);
            }

            Set(ScanModeCode, ScanMode);
            Set(TriggerTimeoutCode, TriggerTimeoutMs.ToString());
            Set(RereadDelayCode, RereadDelayMs.ToString());
            if (Val(ConDecodeTimeoutCode) != DecodeTimeoutMs.ToString() || Val(TriDecodeTimeoutCode) != DecodeTimeoutMs.ToString())
            {
                cmds.Add(ConDecodeTimeoutCode + DecodeTimeoutMs);
                cmds.Add(ConDecodeTimeout2Code + DecodeTimeoutMs * 2);
                cmds.Add(TriDecodeTimeoutCode + DecodeTimeoutMs);
                cmds.Add(TriDecodeTimeout2Code + DecodeTimeoutMs * 2);
            }
            Set(MultiCodeCode, MultiCodeCount.ToString());
            Set(TriggerCmdCode, ShiYinProtocol.DisplayToHex(TriggerCommandText));
            Set(ReleaseCmdCode, ShiYinProtocol.DisplayToHex(ReleaseCommandText));

            foreach (var (key, codes) in SymbologyCodes)
            {
                var v = _symbologies[key] ? "1" : "0";
                foreach (var c in codes) Set(c, v);
            }

            Set(PrefixCode, AffixToValue(PrefixText));
            Set(SuffixCode, AffixToValue(SuffixText));
            Set(ReadFailPromptCode, ReadFailPrompt ? "1" : "0");
            Set(ReadFailTextCode, ShiYinProtocol.DisplayToHex(ReadFailText));

            Set(IllumCode, IllumOn ? "3" : "0");
            Set(IllumLevelCode, IllumLevel);
            Set(LightControlCode, ((Light1 ? 1 : 0) | (Light2 ? 2 : 0) | (Light3 ? 4 : 0) | (Light4 ? 8 : 0)).ToString());
            Set(AimerCode, Aimer);
            Set(BeepCode, BeepTone.ToString());

            Set(ConExpModeCode, ConExposureMode);
            Set(ConExposureCode, ConExposure.ToString());
            Set(ConGainCode, ConGain.ToString());
            Set(TriExpModeCode, TriExposureMode);
            Set(TriExposureCode, TriExposure.ToString());
            Set(TriGainCode, TriGain.ToString());
            Set(FocusCode, Focus.ToString());

            return cmds;
        }

        private async Task SaveAsync()
        {
            if (IsBusy) return;
            var changes = BuildChanges();
            if (changes.Count == 0)
            {
                SetStatus("Không có thay đổi nào để lưu.");
                return;
            }

            IsBusy = true;
            try
            {
                SetStatus($"Đang ghi {changes.Count} tham số vào đầu đọc...");
                var result = await _service.SaveAsync(changes);

                var rejected = result.Rejected.ToList();
                if (rejected.Count > 0)
                    _log("Đầu đọc từ chối: " + string.Join(", ", rejected.Select(r => r.Code + (r.Status == ReplyStatus.Nak ? " (NAK)" : " (không hỗ trợ)"))));

                // Đọc lại để chắc chắn giá trị trên màn hình đúng với đầu đọc
                _original = await _service.ReadValuesAsync(ValueCodes);
                ApplyValuesToUi();

                var accepted = result.Accepted.Count();
                if (rejected.Count == 0)
                    SetStatus($"Đã lưu {accepted} tham số vào đầu đọc lúc {DateTime.Now:HH:mm:ss}" + (result.Persisted ? "." : " (chưa xác nhận lưu bền)."));
                else
                    SetStatus($"Đã lưu {accepted} tham số, {rejected.Count} bị từ chối (xem nhật ký).", true);
            }
            catch (Exception ex)
            {
                SetStatus($"Không lưu được cấu hình: {ex.Message}", true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Điền các giá trị phù hợp quầy thu ngân; người dùng bấm Lưu để ghi xuống đầu đọc.</summary>
        private void ApplySupermarketPreset()
        {
            ScanMode = "8";
            // Đọc liên tục: cùng một mã nằm yên trước đầu đọc sẽ được gửi lại sau mỗi khoảng này,
            // nên để dài để hàng không bị cộng thêm khi thu ngân chưa kịp lấy ra.
            RereadDelayMs = 1500;
            MultiCodeCount = 1;
            // Chỉ bật loại mã hàng hóa thật sự dùng. Code 39, I25, Code 93 không có số kiểm tra
            // nên hay đọc nhầm từ chữ in / vạch kẻ trên bao bì.
            string[] keep = ["EAN", "UPC", "Code128", "QR"];
            foreach (var key in _symbologies.Keys.ToList())
                _symbologies[key] = keep.Contains(key);
            NotifyAllSymbologies();
            PrefixText = "";
            SuffixText = "[CR][LF]";
            ReadFailPrompt = false;
            IllumOn = true;
            SetStatus("Đã điền cấu hình mẫu cho quầy thu ngân: đọc liên tục, chỉ bật EAN/UPC/Code128/QR, chống lặp 1500 ms, kết thúc CR+LF. Bấm \"Lưu vào đầu đọc\" để áp dụng.");
        }

        private async Task RestoreFactoryAsync()
        {
            var ok = MessageBox.Show(
                "Khôi phục toàn bộ cấu hình gốc của nhà sản xuất?\n\nĐầu đọc sẽ khởi động lại và có thể mất vài giây để kết nối lại. " +
                "Địa chỉ IP của đầu đọc không thay đổi.",
                "Khôi phục mặc định", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (ok != MessageBoxResult.Yes) return;

            IsBusy = true;
            try
            {
                SetStatus("Đang gửi lệnh khôi phục mặc định...");
                await _service.RestoreFactoryAsync();
                HasConfig = false;
                SetStatus("Đã gửi lệnh khôi phục mặc định. Chờ đầu đọc khởi động lại rồi bấm \"Đọc từ đầu đọc\".");
            }
            catch (Exception ex)
            {
                SetStatus($"Không khôi phục được: {ex.Message}", true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Bắt đầu / dừng đọc. Chế độ trigger: gửi lệnh trigger / nhả (STX F4/F5 ETX).
        /// Chế độ đọc liên tục: đầu đọc không có lệnh dừng, nên "dừng" = tạm chuyển sang chế độ trigger
        /// (lệnh ghi tạm, mất khi đầu đọc khởi động lại), "bắt đầu" = bật lại đọc liên tục.
        /// </summary>
        private async Task TriggerAsync(bool start)
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                bool continuous = ScanMode == "8";
                if (start)
                {
                    if (continuous)
                    {
                        await _service.ApplyTemporaryAsync([ScanModeCode + "8"]);
                        SetStatus("Đầu đọc đã đọc liên tục trở lại.");
                    }
                    else
                    {
                        await _service.TriggerAsync();
                        SetStatus($"Đã gửi lệnh kích đọc. Đầu đọc sẽ đọc tới khi thấy mã, khi bấm Dừng đọc, hoặc hết {TriggerTimeoutMs} ms.");
                    }
                }
                else
                {
                    await _service.ReleaseAsync();
                    await _service.ApplyTemporaryAsync([ScanModeCode + "0"]);
                    SetStatus(continuous
                        ? "Đã tạm dừng đọc liên tục. Bấm Bắt đầu đọc để đọc lại; khởi động lại đầu đọc cũng sẽ đọc lại theo cấu hình đã lưu."
                        : "Đã gửi lệnh dừng đọc.");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Lỗi gửi lệnh: {ex.Message}", true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static void OnUi(Action action) =>
            Application.Current?.Dispatcher.BeginInvoke(action);
    }
}
