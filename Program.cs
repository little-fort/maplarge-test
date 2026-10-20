using TestProject.Helpers;
using TestProject.Services;

namespace TestProject 
{
    public class Program 
    {
        public static void Main(string[] args) 
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton<DirectorySizeService>();
            builder.Services.AddSingleton<FileActionService>();

            // The folder the browser can reach
            PathResolver.Configure(builder.Configuration["RootFolder"], builder.Environment.WebRootPath);

            var app = builder.Build();

            // Configure the HTTP request pipeline.

            app.UseHttpsRedirection();

            // Defense against cross-site scripting
            app.Use(async (context, next) =>
            {
                if (!HttpMethods.IsGet(context.Request.Method)
                    && !HttpMethods.IsHead(context.Request.Method)
                    && context.Request.Headers.XRequestedWith != "XMLHttpRequest")
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                await next(context);
            });

            app.UseStaticFiles();

            app.MapControllers();

            app.Run();
        }
    }
}