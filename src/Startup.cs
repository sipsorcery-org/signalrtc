//-----------------------------------------------------------------------------
// Filename: Startup.cs
//
// Description: Startup and configuration for SIP/Web server application. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using signalrtc.DataAccess;

namespace signalrtc
{
    public class Startup
    {
        public const string COOKIE_SCHEME = "signalrtc";
        public const string CORS_POLICY_NAME = "SignalRTCPolicy";

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging();

            var dbContextOptions = Configuration.GetConnectionString("SIPAssetsLite");

            // Explicitly register DbContextOptions
            services.AddSingleton(provider =>
            {
                var optionsBuilder = new DbContextOptionsBuilder<SIPAssetsDbContext>();
                optionsBuilder.UseSqlite(dbContextOptions);
                return optionsBuilder.Options;
            });

            // DB Context factory is used by the SIP servers.
            services.AddDbContextFactory<SIPAssetsDbContext>(options =>
                options.UseSqlite(dbContextOptions));

            // DB Context is used directly by web API controllers.
            services.AddDbContext<SIPAssetsDbContext>(options =>
                options.UseSqlite(dbContextOptions));

            //services.AddDistributedSqlServerCache(opts =>
            //{
            //    opts.ConnectionString = Configuration.GetConnectionString("SIPAssetsLite");
            //    opts.SchemaName = "dbo";
            //    opts.TableName = "SessionCache";
            //});

            services.AddSession(options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = false;
                options.Cookie.Name = COOKIE_SCHEME;
            });

            if (Program.TlsCertificate != null)
            {
                services.AddSingleton(Program.TlsCertificate);
            }

            services.AddSingleton(typeof(SIPDialPlanManager));
            services.AddSingleton(typeof(SIPHostedService));
            services.AddHostedService<SIPHostedService>();

            services.AddCors(options =>
            {
                options.AddPolicy(name: CORS_POLICY_NAME,
                    builder =>
                    {
                        builder.WithOrigins("*")
                               .AllowAnyHeader()
                                .AllowAnyMethod();
                    });
            });

            services.AddControllers()
                .AddNewtonsoftJson(options =>
              {
                  options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
                  options.SerializerSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                  //options.SerializerSettings.Error = (object sender, Newtonsoft.Json.Serialization.ErrorEventArgs args) =>
                  //{
                  //    System.Console.WriteLine($"JSON errror: {args.ErrorContext.Error}");
                  //};
              }
            );
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "signalrtc", Version = "v1" });
            });

            services.AddControllersWithViews();
            services.AddMvc();

            services.AddAuthentication(COOKIE_SCHEME) // Sets the default scheme to cookies
                .AddCookie(COOKIE_SCHEME, options =>
                {
                    options.AccessDeniedPath = "/home/accessdenied";
                    options.LoginPath = "/home/index";
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "demo v1"));
            }
            else
            {
                app.UseHsts();
            }

            //app.UseHttpsRedirection();
            app.UseRouting();
            app.UseCors();
            app.UseStaticFiles();
            app.UseSession();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/version", async context =>
                {
                    await context.Response.WriteAsync(Program.GetVersion());
                });

                endpoints.MapControllers().RequireCors(CORS_POLICY_NAME);

                endpoints.MapControllerRoute(
                      name: "areas",
                      pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}"
                    );

                endpoints.MapControllerRoute(
                   name: "echo",
                   pattern: "echo/{action}/{id}");

                endpoints.MapControllerRoute(
                   name: "default",
                   pattern: "{controller=Home}/{action=Index}/{id?}");
            });

            SIPSorcery.LogFactory.Set(app.ApplicationServices.GetService<ILoggerFactory>());
        }
    }
}
