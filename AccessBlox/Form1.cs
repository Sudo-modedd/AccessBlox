using System;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Win32;
using IWshRuntimeLibrary; // Требуется COM-ссылка: Microsoft Script Control
using System.Drawing;
using System.Threading;
using System.Security.Principal;

namespace AccessBlox
{
    public partial class Form1 : Form
    {
        // ------------------------------------------------------
        // КОНСТАНТЫ
        // ------------------------------------------------------
        private const string BypassToolName = "winws.exe";
        private const string GameFilterPorts = "49152-65535";

        private const string SilentArgument = "run_silent";
        private const string SilentStudioArgument = "run_studio_silent";

        private const string PlayerShortcutName = "Roblox (с обходом).lnk";
        private const string StudioShortcutName = "Roblox Studio (с обходом).lnk";

        private const string PlayerCustomIconName = "robloxicon.ico";
        private const string StudioCustomIconName = "robloxstudio.ico";


        private Process _bypassProcess;
        private Process _robloxProcess;
        private bool _isSilentMode = false;
        private bool _isStudioSilentMode = false;
        private bool _isClosing = false;

        // ------------------------------------------------------
        // 0. ИНИЦИАЛИЗАЦИЯ, ЛОГГЕР И ПРОВЕРКИ
        // ------------------------------------------------------

        public Form1()
        {
            // Проверка прав администратора
            if (!IsAdministrator())
            {
                MessageBox.Show("Для корректной работы приложение AccessBlox должно быть запущено от имени Администратора.", "Ошибка доступа", MessageBoxButtons.OK, MessageBoxIcon.Error);
                System.Windows.Forms.Application.Exit();
                return;
            }

            string[] args = Environment.GetCommandLineArgs();

            // 💡 ИСПРАВЛЕННЫЙ БЛОК ОБРАБОТКИ АРГУМЕНТОВ
            if (args.Length > 1)
            {
                // Берем первый аргумент, удаляем пробелы и приводим к нижнему регистру
                string arg = args[1].Trim().ToLower();

                if (arg == SilentStudioArgument)
                {
                    _isSilentMode = true;
                    _isStudioSilentMode = true;
                    Debug.WriteLine("AccessBlox: Тихий режим для Studio активирован.");
                }
                else if (arg == SilentArgument)
                {
                    _isSilentMode = true;
                    Debug.WriteLine("AccessBlox: Тихий режим для Player активирован.");
                }
            }

            InitializeComponent();
            guna2CircleButton1.Text = "OFF";

            // ИНИЦИАЛИЗАЦИЯ ТРЕЯ 
            if (notifyIcon1 != null)
            {
                notifyIcon1.Icon = this.Icon;
                notifyIcon1.Text = "AccessBlox (Обход Роблокс) - OFF";
                notifyIcon1.Visible = true;
                notifyIcon1.DoubleClick += new EventHandler(notifyIcon1_DoubleClick);

                if (contextMenuStrip1 != null)
                {
                    notifyIcon1.ContextMenuStrip = contextMenuStrip1;
                    contextMenuStrip1.Items.Add("Показать окно", null, (s, e) => notifyIcon1_DoubleClick(null, null));
                    contextMenuStrip1.Items.Add("Выход (отключить обход)", null, (s, e) => ExitApplication());
                }
            }

            if (!_isSilentMode)
            {
                // Загрузка настроек 
                // guna2ToggleSwitch1.Checked = Properties.Settings.Default.SilentModeEnabled;
                // guna2ToggleSwitch2.Checked = Properties.Settings.Default.AutoStartRoblox;
            }

            // Скрываем основную форму, если это тихий режим
            if (_isSilentMode)
            {
                this.Visible = false;
                this.ShowInTaskbar = false;
                this.WindowState = FormWindowState.Minimized;
            }

            // Запускаем всю логику
            RunApplicationLogic();
        }

        private void UpdateStatusLabel(string message, bool isError = false)
        {
            // Используем Invoke, если вызывается из другого потока
            if (this.label4.InvokeRequired)
            {
                this.label4.Invoke(new System.Action(() => UpdateStatusLabel(message, isError)));
                return;
            }

            string logEntry = $"[{DateTime.Now.ToShortTimeString()}] {message}";

            this.label4.Text = logEntry;
            this.label4.ForeColor = isError ? Color.Red : Color.White;

            Debug.WriteLine(logEntry);
        }

        private bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        // ------------------------------------------------------
        // 1. ПОИСК ПУТЕЙ И АГРЕССИВНЫЕ АРГУМЕНТЫ WINWS
        // ------------------------------------------------------

        private string FindRobloxPath()
        {
            // Логика поиска пути Roblox Player
            try
            {
                string localAppDataVersionsDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
                if (System.IO.Directory.Exists(localAppDataVersionsDirectory))
                {
                    var latestVersionDir = System.IO.Directory.GetDirectories(localAppDataVersionsDirectory, "version-*", System.IO.SearchOption.TopDirectoryOnly)
                                                                 .OrderByDescending(d => new System.IO.DirectoryInfo(d).CreationTime)
                                                                 .FirstOrDefault();
                    if (latestVersionDir != null)
                    {
                        string playerPath = System.IO.Path.Combine(latestVersionDir, "RobloxPlayerBeta.exe");
                        if (System.IO.File.Exists(playerPath))
                        {
                            if (!_isSilentMode) UpdateStatusLabel($"✅ Roblox Player найден: {playerPath}");
                            return playerPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка поиска Roblox Player: {ex.Message}");
            }

            // Добавлено логгирование при неудаче
            if (!_isSilentMode) UpdateStatusLabel("❌ Roblox Player не найден в папках Versions.", true);
            return null;
        }

        private string FindRobloxStudioPath()
        {
            // 1. Поиск в Реестре Windows (наиболее надежный способ)
            try
            {
                string registryPath = @"SOFTWARE\WOW6432Node\Roblox\RobloxStudio";

                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryPath))
                {
                    if (key != null)
                    {
                        string studioPath = key.GetValue("InstallPath")?.ToString();

                        if (!string.IsNullOrEmpty(studioPath))
                        {
                            string studioExe = System.IO.Path.Combine(studioPath, "RobloxStudioLauncherBeta.exe");
                            if (System.IO.File.Exists(studioExe))
                            {
                                if (!_isSilentMode) UpdateStatusLabel($"✅ Studio найдена в Реестре: {studioExe}");
                                return studioExe;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка чтения реестра Studio: {ex.Message}");
            }

            // 2. АГРЕССИВНЫЙ ПОИСК: Проверяем ВСЕ папки version-*
            try
            {
                string localAppDataVersionsDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");

                if (System.IO.Directory.Exists(localAppDataVersionsDirectory))
                {
                    var versionDirs = System.IO.Directory.GetDirectories(localAppDataVersionsDirectory, "version-*", System.IO.SearchOption.TopDirectoryOnly);

                    // Сортируем по дате создания, чтобы начать с самой новой (оптимизация)
                    var sortedDirs = versionDirs.OrderByDescending(d => new System.IO.DirectoryInfo(d).CreationTime);

                    foreach (var dir in sortedDirs)
                    {
                        string launcherExePath = System.IO.Path.Combine(dir, "RobloxStudioLauncherBeta.exe");
                        string mainExePath = System.IO.Path.Combine(dir, "RobloxStudioBeta.exe");

                        if (System.IO.File.Exists(launcherExePath))
                        {
                            if (!_isSilentMode) UpdateStatusLabel($"✅ Studio Launcher найдена в папке: {dir}");
                            return launcherExePath;
                        }
                        else if (System.IO.File.Exists(mainExePath))
                        {
                            if (!_isSilentMode) UpdateStatusLabel($"✅ RobloxStudioBeta найдена в папке: {dir}");
                            return mainExePath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка поиска в Versions: {ex.Message}");
            }

            if (!_isSilentMode) UpdateStatusLabel("❌ Roblox Studio не найдена ни в Реестре, ни в папках Versions.", true);
            return null;
        }


        private string GetFullBypassArguments()
        {
            string BIN_DIR = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "bin");
            string LISTS_PATH = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "lists");

            if (!System.IO.Directory.Exists(LISTS_PATH))
            {
                if (!_isSilentMode) UpdateStatusLabel($"Папка 'lists' не найдена.", true);
                return "";
            }

            string GF = GameFilterPorts;

            // 💣 МАКСИМАЛЬНО АГРЕССИВНАЯ КОНФИГУРАЦИЯ (ОСТАВЛЕНА БЕЗ ИЗМЕНЕНИЙ)
            string args =
            $@"--wf-tcp=80,443,2053,2083,2087,2096,8443,{GF} --wf-udp=443,19294-19344,50000-50100,{GF} " +
            $@"--filter-udp=443 --hostlist=""{LISTS_PATH}\list-general.txt"" --hostlist-exclude=""{LISTS_PATH}\list-exclude.txt"" --ipset-exclude=""{LISTS_PATH}\ipset-exclude.txt"" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=""{BIN_DIR}\quic_initial_www_google_com.bin"" --new " +
            $@"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-repeats=6 --new " +
            $@"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=multisplit --dpi-desync-split-seqovl=568 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=""{BIN_DIR}\tls_clienthello_4pda_to.bin"" --new " +
            $@"--filter-tcp=443 --hostlist=""{LISTS_PATH}\list-google.txt"" --ip-id=zero --dpi-desync=multisplit --dpi-desync-split-pos=2,sniext+1 --dpi-desync-split-seqovl=679 --dpi-desync-split-seqovl-pattern=""{BIN_DIR}\tls_clienthello_www_google_com.bin"" --new " +
            $@"--filter-tcp=80,443 --hostlist=""{LISTS_PATH}\list-general.txt"" --hostlist-exclude=""{LISTS_PATH}\list-exclude.txt"" --ipset-exclude=""{LISTS_PATH}\ipset-exclude.txt"" --dpi-desync=hostfakesplit --dpi-desync-repeats=4 --dpi-desync-fooling=ts,md5sig --dpi-desync-hostfakesplit-mod=host=ozon.ru --new " +
            $@"--filter-udp=443 --ipset=""{LISTS_PATH}\ipset-all.txt"" --hostlist-exclude=""{LISTS_PATH}\list-exclude.txt"" --ipset-exclude=""{LISTS_PATH}\ipset-exclude.txt"" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=""{BIN_DIR}\quic_initial_www_google_com.bin"" --new " +
            $@"--filter-tcp=80,443,{GF} --ipset=""{LISTS_PATH}\ipset-all.txt"" --hostlist-exclude=""{LISTS_PATH}\list-exclude.txt"" --ipset-exclude=""{LISTS_PATH}\ipset-exclude.txt"" --dpi-desync=syndata --new " +
            $@"--filter-udp={GF} --ipset=""{LISTS_PATH}\ipset-all.txt"" --ipset-exclude=""{LISTS_PATH}\ipset-exclude.txt"" --dpi-desync=fake --dpi-desync-ttl=64 --dpi-desync-repeats=12 --dpi-desync-any-protocol=1 --dpi-desync-fake-unknown-udp=""{BIN_DIR}\quic_initial_www_google_com.bin"" --dpi-desync-cutoff=n2";

            return args;
        }

        // ------------------------------------------------------
        // 2. УПРАВЛЕНИЕ ЗАПУСКОМ (RunApplicationLogic)
        // ------------------------------------------------------

        private void RunApplicationLogic()
        {
            // Запускаем асинхронный мониторинг состояния winws.exe
            _ = CheckBypassProcessStatusAsync();

            if (_isSilentMode)
            {
                SplashScreen splash = null;
                try
                {
                    // Создаем и показываем сплеш-скрин
                    splash = new SplashScreen();
                    splash.Show();

                    // --- СТАДИЯ 1: ИНИЦИАЛИЗАЦИЯ (20%) ---
                    splash.SetStatus("AccessBlox: Загрузка конфигурации...");
                    splash.SetProgress(20);
                    Thread.Sleep(100);

                    // --- СТАДИЯ 2: ЗАПУСК ОБХОДА (60%) ---
                    splash.SetStatus("AccessBlox: Активация WINWS (Обход)...");
                    splash.SetProgress(60);
                    Thread.Sleep(100);

                    // Выполнение основной работы: запуск обхода и клиента Roblox
                    StartBypassTool(true); // Передаем true, чтобы запустить клиента сразу

                    // Проверка, успешно ли запущен обход
                    if (_bypassProcess != null && !_bypassProcess.HasExited)
                    {
                        // --- СТАДИЯ 3: ЗАПУСК КЛИЕНТА (100%) ---
                        string clientName = _isStudioSilentMode ? "Roblox Studio" : "Roblox Player";
                        splash.SetStatus($"AccessBlox: Запуск {clientName}. Готово.");
                        splash.SetProgress(100);
                        Thread.Sleep(500); // Дать пользователю увидеть 100%

                        // Логика немедленного выхода для Studio в тихом режиме
                        // Studio запускается и остается открытой, но AccessBlox может закрыться,
                        // оставляя обход включенным до закрытия Studio.
                        if (_isStudioSilentMode)
                        {
                            // В тихом режиме Studio мы запускаем ее, но не мониторим ее процесс,
                            // чтобы не закрывать AccessBlox, пока пользователь не закроет сам Studio/winws.
                            // Для студии лучше остаться в трее, или вообще не закрывать AccessBlox,
                            // если Studio запущена с обходом. Текущая логика System.Windows.Forms.Application.Exit() 
                            // завершит работу обхода сразу, если нет мониторинга Player.
                            // НО, поскольку winws будет мониториться через CheckBypassProcessStatusAsync,
                            // и если _robloxProcess не назначен для Studio, то обход останется
                            // до тех пор, пока пользователь не закроет winws вручную, или AccessBlox не закроется сам.
                            // Для Studio лучше выйти, чтобы пользователь мог открыть ее несколько раз без конфликтов.
                            System.Windows.Forms.Application.Exit();
                        }
                    }
                    else
                    {
                        // Обход не запустился, показываем ошибку
                        splash.SetStatus("Ошибка: Не удалось запустить WINWS. Проверьте права Администратора и файлы.");
                        Thread.Sleep(3000); // Оставить на 3 секунды для чтения ошибки
                        System.Windows.Forms.Application.Exit();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Ошибка в тихом режиме: {ex.Message}");
                    System.Windows.Forms.Application.Exit();
                }
                finally
                {
                    // 4. Закрытие сплеш-скрина
                    if (splash != null && splash.IsHandleCreated)
                    {
                        // Безопасное закрытие
                        splash.Invoke(new Action(() => splash.Close()));
                    }
                }
            }
            else
            {
                // Обычный режим (UI уже отображается)
                UpdateStatusLabel("Приложение готово к работе. Обход не активен.");
            }
        }

        // ------------------------------------------------------
        // 3. МОНИТОРИНГ И КОНТРОЛЬ ПРОЦЕССОВ
        // ------------------------------------------------------

        private async System.Threading.Tasks.Task CheckBypassProcessStatusAsync()
        {
            while (true)
            {
                // Если обход был запущен, но внезапно закрылся (и это не наше собственное закрытие)
                if (_bypassProcess != null && _bypassProcess.HasExited)
                {
                    _bypassProcess = null;
                    if (!_isSilentMode && !_isClosing)
                    {
                        // Обновление UI
                        if (guna2CircleButton1.InvokeRequired)
                        {
                            guna2CircleButton1.Invoke(new System.Action(() => {
                                guna2CircleButton1.Text = "OFF";
                                UpdateStatusLabel("Внешний обход Роблокса был закрыт. Функциональность отключена.", true);
                                if (notifyIcon1 != null) notifyIcon1.Text = "AccessBlox (Обход Роблокс) - OFF";
                            }));
                        }
                    }
                    // Если это был тихий режим и обход закрылся, закрываемся сами
                    if (_isSilentMode) System.Windows.Forms.Application.Exit();
                }
                await System.Threading.Tasks.Task.Delay(5000);
            }
        }

        private void MonitorRobloxPlayerExit(Process robloxProcess)
        {
            try
            {
                if (robloxProcess == null || robloxProcess.HasExited) return;

                robloxProcess.WaitForExit();

                // Если Roblox Player закрылся, останавливаем обход (winws)
                if (_bypassProcess != null && !_bypassProcess.HasExited)
                {
                    StopBypassTool(false); // Останавливаем только обход, не выходим из приложения сразу
                    if (!_isSilentMode && guna2CircleButton1.InvokeRequired)
                    {
                        guna2CircleButton1.Invoke(new System.Action(() => {
                            guna2CircleButton1.Text = "OFF";
                            UpdateStatusLabel("Roblox Player закрыт. Обход Роблокса автоматически остановлен.");
                            if (notifyIcon1 != null) notifyIcon1.Text = "AccessBlox (Обход Роблокс) - OFF";
                        }));
                    }
                }
                // Если тихий режим, закрываем само приложение после выхода Player
                if (_isSilentMode) System.Windows.Forms.Application.Exit();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка мониторинга Roblox: {ex.Message}");
            }
        }

        private void StartBypassTool(bool autoStartClient = false)
        {
            if (_bypassProcess != null && !_bypassProcess.HasExited) return;

            string arguments = GetFullBypassArguments();
            if (string.IsNullOrEmpty(arguments)) return;

            string fullPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "bin", BypassToolName);

            if (!System.IO.File.Exists(fullPath))
            {
                if (!_isSilentMode) UpdateStatusLabel($"❌ Файл обходчика '{BypassToolName}' не найден по пути: {fullPath}", true);
                return;
            }

            try
            {
                _bypassProcess = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = fullPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                _bypassProcess.Start();
                if (!_isSilentMode) guna2CircleButton1.Text = "ON";
                if (!_isSilentMode) UpdateStatusLabel("✅ WINWS (Полный обход) запущен (скрыто).");
                if (!_isSilentMode && notifyIcon1 != null) notifyIcon1.Text = "AccessBlox (Обход Роблокс) - ON";

                // Запускаем Roblox, если запрошено
                if (autoStartClient)
                {
                    StartRoblox(_isStudioSilentMode);
                }
            }
            catch (Exception ex)
            {
                if (!_isSilentMode) UpdateStatusLabel($"Ошибка запуска WINWS: {ex.Message}", true);
                _bypassProcess = null;
            }
        }
        private void StopBypassTool(bool killApp = true)
        {
            if (killApp) _isClosing = true;

            // Закрываем Roblox Player
            if (_robloxProcess != null && !_robloxProcess.HasExited)
            {
                try { _robloxProcess.Kill(); _robloxProcess.Dispose(); } catch (Exception) { }
            }
            _robloxProcess = null;

            // Закрываем обход
            if (_bypassProcess != null && !_bypassProcess.HasExited)
            {
                try { _bypassProcess.Kill(); _bypassProcess.Dispose(); _bypassProcess = null; } catch (Exception) { }
            }

            if (killApp) _isClosing = false;
            if (killApp && !_isSilentMode)
            {
                UpdateStatusLabel("❌ Обход и Roblox остановлены.");
                guna2CircleButton1.Text = "OFF";
                if (notifyIcon1 != null) notifyIcon1.Text = "AccessBlox (Обход Роблокс) - OFF";
            }
        }

        private void StartRoblox(bool isStudio)
        {
            string robloxPath = isStudio ? FindRobloxStudioPath() : FindRobloxPath();
            string programName = isStudio ? "Roblox Studio" : "Roblox Player";

            if (string.IsNullOrEmpty(robloxPath))
            {
                if (!_isSilentMode) UpdateStatusLabel($"Не удалось найти путь к {programName}. Запустите его вручную.", true);
                return;
            }

            try
            {
                Process p = System.Diagnostics.Process.Start(robloxPath);

                if (p == null && !_isSilentMode)
                {
                    UpdateStatusLabel($"Запуск {programName}, возможно, не удался. Проверьте запущенные процессы.", true);
                }
                else if (p != null && !_isSilentMode)
                {
                    UpdateStatusLabel($"Запущен {programName}.");
                }


                if (_isSilentMode && (p == null || p.HasExited))
                {
                    // Если в тихом режиме запуск провалился, выходим
                    System.Windows.Forms.Application.Exit();
                }

                // Запускаем мониторинг только для Roblox Player
                if (!isStudio && p != null)
                {
                    _robloxProcess = p;
                    Task.Run(() => MonitorRobloxPlayerExit(p));
                }
            }
            catch (Exception ex)
            {
                if (!_isSilentMode) UpdateStatusLabel($"Не удалось запустить {programName}: {ex.Message}", true);
            }
        }

        // ------------------------------------------------------
        // 4. КОНТРОЛЬ ЯРЛЫКА И НАСТРОЙКИ
        // ------------------------------------------------------

        private void CreateShortcut(bool create, bool isStudio)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutName = isStudio ? StudioShortcutName : PlayerShortcutName;
                string shortcutPath = System.IO.Path.Combine(desktopPath, shortcutName);
                string programName = isStudio ? "Roblox Studio" : "Roblox Player";

                // Сначала находим путь (эти функции гарантированно вернут null, если .exe не найден)
                string targetPath = isStudio ? FindRobloxStudioPath() : FindRobloxPath();

                string description = isStudio ? "Запускает Roblox Studio с автоматическим обходом." : "Запускает Roblox Player с автоматическим обходом.";

                if (create)
                {
                    // 1. Проверка существования исполняемого файла
                    if (string.IsNullOrEmpty(targetPath))
                    {
                        if (!_isSilentMode) UpdateStatusLabel($"❌ Не удалось создать ярлык '{shortcutName}': Не найден исполняемый файл {programName}.", true);
                        return;
                    }

                    // 2. ИСПРАВЛЕНИЕ: Удалена проверка File.Exists, чтобы ярлык всегда пересоздавался/обновлялся.
                    // if (System.IO.File.Exists(shortcutPath)) return; // <-- ЭТУ СТРОКУ УДАЛИЛИ


                    IWshRuntimeLibrary.WshShell wshShell = new IWshRuntimeLibrary.WshShell();
                    IWshRuntimeLibrary.IWshShortcut shortcut = (IWshRuntimeLibrary.IWshShortcut)wshShell.CreateShortcut(shortcutPath);

                    shortcut.Description = description;
                    shortcut.TargetPath = System.Windows.Forms.Application.ExecutablePath;

                    shortcut.Arguments = isStudio ? SilentStudioArgument : SilentArgument;

                    // Логика установки иконки (остается без изменений)
                    string customIconFileName = isStudio ? StudioCustomIconName : PlayerCustomIconName;
                    string customIconPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, customIconFileName);
                    string finalIconPath;

                    if (System.IO.File.Exists(customIconPath))
                    {
                        finalIconPath = customIconPath;
                    }
                    else if (!string.IsNullOrEmpty(targetPath))
                    {
                        // Используем иконку из найденного .exe
                        finalIconPath = targetPath + ",0";
                    }
                    else
                    {
                        // Используем иконку из нашей программы (запасной вариант)
                        finalIconPath = System.Windows.Forms.Application.ExecutablePath + ",0";
                    }

                    shortcut.IconLocation = finalIconPath;

                    shortcut.Save();
                    if (!_isSilentMode) UpdateStatusLabel($"✅ Ярлык '{shortcutName}' создан на рабочем столе.");
                }
                else
                {
                    // Логика удаления ярлыка (остается без изменений)
                    if (System.IO.File.Exists(shortcutPath))
                    {
                        System.IO.File.Delete(shortcutPath);
                        if (!_isSilentMode) UpdateStatusLabel($"Ярлык '{shortcutName}' удален с рабочего стола.");
                    }
                }
            }
            catch (Exception ex)
            {
                // Очень важный лог для отладки проблем с COM-объектами
                if (!_isSilentMode) UpdateStatusLabel($"КРИТИЧЕСКАЯ ОШИБКА ЯРЛЫКА: {ex.Message}\nПроверьте, добавлена ли COM-ссылка 'Microsoft Script Host Object Model'.", true);
            }
        }

        // ------------------------------------------------------
        // 5. UI И ОБРАБОТЧИКИ СОБЫТИЙ
        // ------------------------------------------------------

        private void ExitApplication()
        {
            StopBypassTool();
            if (notifyIcon1 != null) notifyIcon1.Visible = false;
            System.Windows.Forms.Application.Exit();
        }

        private void guna2CircleButton1_Click(object sender, EventArgs e)
        {
            if (guna2CircleButton1.Text == "ON")
            {
                StopBypassTool(true);
            }
            else
            {
                // Запуск обхода. Если переключатель AutoStartRoblox включен, запускается и Roblox Player.
                // NOTE: guna2ToggleSwitch2.Checked не используется в тихом режиме
                bool autoStart = guna2ToggleSwitch2.Checked;
                StartBypassTool(autoStart);
            }
        }

        private void guna2ButtonRoblox_Click(object sender, EventArgs e)
        {
            if (guna2CircleButton1.Text == "OFF")
            {
                UpdateStatusLabel("Сначала включите обход Роблокса.", true);
                return;
            }
            StartRoblox(false); // Запускаем Player
        }

        private void guna2ButtonRobloxStudio_Click(object sender, EventArgs e)
        {
            if (guna2CircleButton1.Text == "OFF")
            {
                UpdateStatusLabel("Сначала включите обход Роблокса.", true);
                return;
            }
            StartRoblox(true); // Запускаем Studio
        }


        private void guna2ToggleSwitch1_CheckedChanged(object sender, EventArgs e)
        {
            // Здесь должна быть логика сохранения настройки SilentModeEnabled
            // Properties.Settings.Default.SilentModeEnabled = guna2ToggleSwitch1.Checked;
            // Properties.Settings.Default.Save();

            CreateShortcut(guna2ToggleSwitch1.Checked, false); // Player
            CreateShortcut(guna2ToggleSwitch1.Checked, true);  // Studio
        }

        private void guna2ToggleSwitch2_CheckedChanged(object sender, EventArgs e)
        {
            // Здесь должна быть логика сохранения настройки AutoStartRoblox
            // Properties.Settings.Default.AutoStartRoblox = guna2ToggleSwitch2.Checked;
            // Properties.Settings.Default.Save();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_bypassProcess != null && !_bypassProcess.HasExited && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();

                if (notifyIcon1 != null)
                {
                    notifyIcon1.ShowBalloonTip(3000, "AccessBlox", "Приложение свернуто в трей и продолжает обход.", ToolTipIcon.Info);
                }
            }
            else if (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.ApplicationExitCall || e.CloseReason == CloseReason.WindowsShutDown)
            {
                StopBypassTool();
                if (notifyIcon1 != null) notifyIcon1.Visible = false;

                if (e.CloseReason != CloseReason.WindowsShutDown)
                {
                    System.Windows.Forms.Application.Exit();
                }
            }
        }

        // Восстановление окна из трея
        private void notifyIcon1_DoubleClick(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
        }

        // Для кнопки "Свернуть"
        private void guna2Button2_Click(object sender, EventArgs e)
        {
            this.WindowState = System.Windows.Forms.FormWindowState.Minimized;
            this.Hide();
        }

        // Для кнопки "Выход"
        private void guna2Button1_Click(object sender, EventArgs e)
        {
            ExitApplication();
        }

        // Пустые методы, созданные дизайнером:
        private void label1_Click(object sender, EventArgs e) { /* Пусто */ }
    }
}