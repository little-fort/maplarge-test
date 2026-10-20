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

            app.UseStaticFiles();

            app.MapControllers();

            app.Run();
        }
    }
}