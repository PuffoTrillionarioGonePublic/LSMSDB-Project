using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Website.Models.Settings;
using Website.Services;

namespace Website
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // https://docs.microsoft.com/en-us/aspnet/core/tutorials/first-mongo-app?view=aspnetcore-5.0&tabs=visual-studio
            services.Configure<MongoDatabaseSettings> (
                Configuration.GetSection(nameof(MongoDatabaseSettings)));
            services.AddSingleton<IMongoDatabaseSettings>(sp =>
                sp.GetRequiredService<IOptions<MongoDatabaseSettings>>().Value);

            services.Configure<RedisSettings>(
                Configuration.GetSection(nameof(RedisSettings)));
            services.AddSingleton<IRedisSettings>(sp =>
                sp.GetRequiredService<IOptions<RedisSettings>>().Value);

            services.Configure<Neo4jSettings>(
                Configuration.GetSection(nameof(Neo4jSettings)));
            services.AddSingleton<INeo4jSettings>(sp =>
                sp.GetRequiredService<IOptions<Neo4jSettings>>().Value);

            services.AddSingleton<Neo4jService>();

            // See here for distributed cache:
            //  MS Doc      https://docs.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-6.0#distributed-redis-cache
            //  MS Doc      https://docs.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.stackexchangerediscacheservicecollectionextensions.addstackexchangerediscache?view=dotnet-plat-ext-3.1
            //  NuGet       https://www.nuget.org/packages/Microsoft.Extensions.Caching.StackExchangeRedis
            //services.AddStackExchangeRedisCache(options => {
            //    // Solution copied from here:
            //    //  https://stackoverflow.com/questions/32459670/resolving-instances-with-asp-net-core-di-from-within-configureservices
            //    var redOps = new RedisSettings();
            //    Configuration.GetSection(nameof(RedisSettings)).Bind(redOps);
            //    options.InstanceName = redOps.InstanceName;
            //    options.Configuration = redOps.ConnectionString;
            //});

            // See here to understand why is a Singleton:
            //  Mongo Doc   https://mongodb.github.io/mongo-csharp-driver/2.8/reference/driver/connecting/#re-use
            //  MS Doc      https://docs.microsoft.com/en-us/aspnet/core/tutorials/first-mongo-app?view=aspnetcore-5.0&tabs=visual-studio-code#add-a-crud-operations-service
            services.AddSingleton<MongoService>();
            services.AddSingleton<UsersService>();
            services.AddSingleton<QuestionsService>();
            services.AddSingleton<TagsService>();
            services.AddSingleton<SynchronizerService>();

            services.AddControllersWithViews();
            services.AddControllers();

            // Userful tutorial:
            // ASP.NET Core 5.0 - Authentication/Authorization - .Net Engineering Forum 2021-01-26
            //  https://www.youtube.com/watch?v=BWa7Mu-oMHk
            // For the Doc see:
            //  https://docs.microsoft.com/en-us/aspnet/core/security/authorization/introduction?view=aspnetcore-5.0
            // Doc for "Use cookie authentication without ASP.NET Core Identity"
            //  https://docs.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-5.0
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options => {
                    options.LoginPath = "/Users/LoginPage";
                    options.Cookie.Name = "BisCookie";
                    // This is a good tutorial to understand events and authentication/authorisation
                    //  https://www.youtube.com/watch?v=BWa7Mu-oMHk&t=2311s
                    options.Events = new CookieAuthenticationEvents()
                    {
                        OnValidatePrincipal = async context =>
                        {
                            // Old (ASP.NET Core 2) but good
                            //  Why checking here are importants
                            //  https://www.meziantou.net/validating-user-with-cookie-authentication-in-asp-net-core-2.htm
                            var userId = context.Principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.SerialNumber)?.Value;
                            if (userId is not null)
                            {
                                var userService = context.HttpContext.RequestServices.GetService<UsersService>();
                                var user = await userService.GetUserById(userId);
                                if (user is null || user.IsCurrentlyBanned())
                                {
                                    context.RejectPrincipal();
                                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                                }
                            }
                        },
                    };
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }
            app.UseStaticFiles();

            app.UseRouting();

            // See:
            //  https://docs.microsoft.com/en-us/aspnet/core/security/authorization/claims?view=aspnetcore-5.0
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
