namespace HASFOB
{
    public class Program
    {
        public static async Task Main ( string [ ] args )
        {
            if ( ShouldShowHelp ( args ) )
            {
                ShowHelp ( );
                return;
            }
            if ( OperatingSystem.IsWindows ( ) )
            {
                if ( args.Contains ( "-i" ) || args.Contains ( "--install" ) )
                {
                    InstallService ( );
                    return;
                }

                if ( args.Contains ( "-u" ) || args.Contains ( "--uninstall" ) )
                {
                    UninstallService ( );
                    return;
                }
            }
            if ( args.Length > 0 )
            {
                Console.WriteLine ( "Unknown parameter(s) detected.\n" );
                ShowHelp ( );
                return;
            }

            Configuration? config = null;
            Logger? logger = null;
            try
            {
                config = ConfigurationLoader.Load ( );
                logger = new Logger ( config );
                logger.WriteLog ( "Service starting. Configuration loaded successfully." );
            }
            catch ( Exception ex )
            {
                LogCriticalError ( ex );
                Environment.Exit ( 1 );
                return;
            }
            if ( config == null )
                return;

            IHostBuilder builder = Host.CreateDefaultBuilder ( args );

            if ( OperatingSystem.IsWindows ( ) )
                builder.UseWindowsService ( );

            builder.ConfigureServices ( ( hostContext, services ) =>
            {
                services.AddSingleton ( config );
                services.AddSingleton ( logger );
                services.AddHttpClient ( );
                services.AddSingleton<SensorDataService> ( );
                services.AddHostedService<Worker> ( );
                services.AddSingleton<TokenReader> ( );
                services.Configure<HostOptions> ( options =>
                {
                    options.ShutdownTimeout = TimeSpan.FromSeconds ( 45 );
                } );
            } );

            builder.ConfigureWebHostDefaults ( webBuilder =>
            {
                webBuilder.UseUrls ( $"http://0.0.0.0:{config.ServicePort}" );
                webBuilder.Configure ( app =>
                {
                    app.UseRouting ( );
                    app.UseEndpoints ( endpoints =>
                    {
                        //Main page
                        endpoints.MapGet ( "/", async context =>
                        {
                            SensorDataService dataService = context.RequestServices.GetRequiredService<SensorDataService> ( );
                            string html = dataService.GetHtmlPage ( );
                            context.Response.ContentType = "text/html; charset=utf-8";
                            await context.Response.WriteAsync ( html );
                        } );

                        //API - all data (sensors and switchs)
                        endpoints.MapGet ( "/api/all", async context =>
                        {
                            SensorDataService dataService = context.RequestServices.GetRequiredService<SensorDataService> ( );
                            context.Response.ContentType = "application/json; charset=utf-8";
                            await context.Response.WriteAsJsonAsync ( dataService.GetAllData ( ) );
                        } );

                        //API - only sensors
                        endpoints.MapGet ( "/api/sensors", async context =>
                        {
                            SensorDataService dataService = context.RequestServices.GetRequiredService<SensorDataService> ( );
                            context.Response.ContentType = "application/json; charset=utf-8";
                            await context.Response.WriteAsJsonAsync ( dataService.GetAllSensors ( ) );
                        } );

                        //API only switchs
                        endpoints.MapGet ( "/api/switches", async context =>
                        {
                            SensorDataService dataService = context.RequestServices.GetRequiredService<SensorDataService> ( );
                            context.Response.ContentType = "application/json; charset=utf-8";
                            await context.Response.WriteAsJsonAsync ( dataService.GetAllSwitches ( ) );
                        } );

                        //Operating the switch
                        endpoints.MapPost ( "/api/switch/toggle", async context =>
                        {
                            try
                            {
                                ToggleRequest? toggleRequest = await context.Request.ReadFromJsonAsync<ToggleRequest> ( );
                                if ( string.IsNullOrEmpty ( toggleRequest?.EntityId ) )
                                {
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsync ( "EntityId is required" );
                                    return;
                                }

                                SensorDataService dataService = context.RequestServices.GetRequiredService<SensorDataService> ( );
                                Configuration config = context.RequestServices.GetRequiredService<Configuration> ( );
                                TokenReader tokenReader = context.RequestServices.GetRequiredService<TokenReader> ( );
                                IHttpClientFactory httpClientFactory = context.RequestServices.GetRequiredService<IHttpClientFactory> ( );

                                string token = tokenReader.GetToken ( config.HomeAssistantTokenFile );
                                if ( string.IsNullOrEmpty ( token ) )
                                {
                                    context.Response.StatusCode = 500;
                                    await context.Response.WriteAsync ( "Token not found" );
                                    return;
                                }

                                var client = httpClientFactory.CreateClient ( );
                                client.BaseAddress = new Uri ( config.HomeAssistantBaseUrl );
                                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue ( "Bearer", token );

                                var payload = new { entity_id = toggleRequest.EntityId };
                                var response = await client.PostAsJsonAsync ( "/api/services/switch/toggle", payload );

                                context.Response.StatusCode = response.IsSuccessStatusCode ? 200 : 500;
                            }
                            catch ( Exception ex )
                            {
                                context.Response.StatusCode = 500;
                                await context.Response.WriteAsync ( $"Error: {ex.Message}" );
                            }
                        } );

                        //Server-Sent Events
                        endpoints.MapGet ( "/api/events", async context =>
                        {
                            SensorDataService dataService = context.RequestServices.GetRequiredService<SensorDataService> ( );
                            SseClient client = dataService.AddClient ( context );
                            try
                            {
                                await client.SendAsync ( dataService.GetAllSensors ( ) );
                                await Task.Delay ( Timeout.Infinite, context.RequestAborted );
                            }
                            catch ( TaskCanceledException )
                            {
                                //Normal exit
                            }
                            finally
                            {
                                client.Close ( );
                                dataService.RemoveClient ( client );
                            }
                        } );
                    } );
                } );
            } );

            await builder.Build ( ).RunAsync ( );
        }

        private static bool ShouldShowHelp ( string [ ] args )
        {
            if ( args.Length == 0 ) 
                return false;
            string [ ] helpArgs = new [ ] { "h", "-h", "--h", "help", "-help", "--help", "/?", "-?", "/h" };
            return args.Any ( arg => helpArgs.Contains ( arg.Trim ( ).ToLower ( ) ) );
        }

        private static void ShowHelp ( )
        {
            if ( OperatingSystem.IsWindows ( ) )
            {
                Console.WriteLine ( "HASFOB (Home Assistant Service for Old Browsers) Windows Service" );
                Console.WriteLine ( "=====================================" );
                Console.WriteLine ( "A background service that collects sensor data and provides" );
                Console.WriteLine ( "a web interface and API for monitoring." );
                Console.WriteLine ( );
                Console.WriteLine ( "Usage:" );
                Console.WriteLine ( " HASFOB.exe [option]" );
                Console.WriteLine ( );
                Console.WriteLine ( "Options:" );
                Console.WriteLine ( " (no parameters) Run the service" );
                Console.WriteLine ( " -i, --install Install as Windows Service" );
                Console.WriteLine ( " -u, --uninstall Uninstall the Windows Service" );
                Console.WriteLine ( " -h, --help Show this help message" );
            }
            else
            {
                Console.WriteLine ( "HASFOB (Home Assistant Service for Old Browsers)" );
                Console.WriteLine ( "=====================================" );
                Console.WriteLine ( "A background service that collects sensor data and provides" );
                Console.WriteLine ( "a web interface and API for monitoring." );
                Console.WriteLine ( );
                Console.WriteLine ( "Usage:" );
                Console.WriteLine ( " HASFOB [option]" );
                Console.WriteLine ( );
                Console.WriteLine ( "Options:" );
                Console.WriteLine ( " (no parameters) Run the application" );
                Console.WriteLine ( " -h, --help Show this help message" );
                Console.WriteLine ( );
                Console.WriteLine ( "Running as a daemon (Debian/Ubuntu example)" );
                Console.WriteLine ( "-------------------------------------------" );
                Console.WriteLine ( "1. Copy application files to:" );
                Console.WriteLine ( "   /opt/HASFOB/" );
                Console.WriteLine ( );
                Console.WriteLine ( "2. Create file:" );
                Console.WriteLine ( "   /etc/systemd/system/hasfob.service" );
                Console.WriteLine ( );
                Console.WriteLine ( "3. Example service file:" );
                Console.WriteLine ( );
                Console.WriteLine ( "   [Unit]" );
                Console.WriteLine ( "   Description=HASFOB Service" );
                Console.WriteLine ( "   After=network.target" );
                Console.WriteLine ( );
                Console.WriteLine ( "   [Service]" );
                Console.WriteLine ( "   WorkingDirectory=/opt/HASFOB" );
                Console.WriteLine ( "   ExecStart=/opt/HASFOB/HASFOB" );
                Console.WriteLine ( "   Restart=always" );
                Console.WriteLine ( );
                Console.WriteLine ( "   [Install]" );
                Console.WriteLine ( "   WantedBy=multi-user.target" );
                Console.WriteLine ( );
                Console.WriteLine ( "4. Enable and start service:" );
                Console.WriteLine ( "   sudo systemctl daemon-reload" );
                Console.WriteLine ( "   sudo systemctl enable hasfob" );
                Console.WriteLine ( "   sudo systemctl start hasfob" );
                Console.WriteLine ( );
                Console.WriteLine ( "5. View service log:" );
                Console.WriteLine ( "   journalctl -u hasfob -f" );
            }
        }

        private static void LogCriticalError ( Exception ex )
        {
            DateTime timestamp = DateTime.Now;
            string errorMessage = $"[{timestamp:HH:mm:ss}] CRITICAL ERROR loading configuration: {ex}";
            if ( OperatingSystem.IsWindows ( ) )
            {
                try
                {
                    using System.Diagnostics.EventLog eventLog = new System.Diagnostics.EventLog ( "Application" );
                    eventLog.Source = "HASFOB";
                    eventLog.WriteEntry ( errorMessage, System.Diagnostics.EventLogEntryType.Error );
                }
                catch { }
            }
            try
            {
                Console.Error.WriteLine ( errorMessage );
                Console.Error.WriteLine ( ex.ToString ( ) );
            }
            catch { }
            try
            {
                string logPath = GetFallbackLogPath ( );
                File.AppendAllText ( logPath, errorMessage + Environment.NewLine + ex.ToString ( ) + Environment.NewLine + Environment.NewLine );
            }
            catch { }
        }

        private static string GetFallbackLogPath ( )
        {
            try
            {
                //string exePath = System.Diagnostics.Process.GetCurrentProcess ( ).MainModule?.FileName ?? AppContext.BaseDirectory;
                string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
                string exeDir = Path.GetDirectoryName ( exePath ) ?? AppContext.BaseDirectory;
                string logsDir = Path.Combine ( exeDir, "Logs" );
                Directory.CreateDirectory ( logsDir );
                string fileName = $"00_CRITICAL_{DateTime.Now:dd_MM_yyyy}.log";
                return Path.Combine ( logsDir, fileName );
            }
            catch
            {
                return Path.Combine ( AppContext.BaseDirectory, "HASFOB_CRITICAL_ERROR.log" );
            }
        }

        private static void InstallService ( )
        {
            if ( !OperatingSystem.IsWindows ( ) )
                return;

            string exePath = Environment.ProcessPath ?? "";

            System.Diagnostics.Process.Start ( new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc",
                Arguments = $"create HASFOB binPath= \"{exePath}\" start= auto",
                Verb = "runas",
                UseShellExecute = true
            } );
        }

        private static void UninstallService ( )
        {
            if ( !OperatingSystem.IsWindows ( ) )
                return;

            System.Diagnostics.Process.Start ( new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc",
                Arguments = "delete HASFOB",
                Verb = "runas",
                UseShellExecute = true
            } );
        }
    }
    public class ToggleRequest
    {
        public string EntityId { get; set; } = string.Empty;
    }
}