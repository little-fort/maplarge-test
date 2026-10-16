using Microsoft.AspNetCore.Mvc;
using System.IO;
using TestProject.Helpers;
using TestProject.Models;

namespace TestProject.Controllers 
{
    [ApiController]
    [Route("[controller]")]
    public class DirectoryController : ControllerBase 
    {

        private readonly ILogger<DirectoryController> _logger;

        public DirectoryController(ILogger<DirectoryController> logger) 
        {
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string path) 
        {
            var app = Environment.CurrentDirectory; //AppDomain.CurrentDomain.BaseDirectory;
            var fullPath = Path.Combine(app, path.TrimStart('/').TrimStart('\\'));
            var directory = string.Empty;
            if (!Directory.Exists(fullPath) && !System.IO.File.Exists(fullPath))
                return NotFound();

            if (System.IO.File.Exists(fullPath))
            {
                // Infer path for directory browser and render file preview
                var file = new FileInfo(fullPath);
                directory = file.Directory?.FullName ?? "/";
            }
            else
                directory = fullPath;

            var dto = new List<ServerObject>();
            var subdirs = Directory.GetDirectories(directory);
            foreach (var dir in subdirs)
            {
                var info = new DirectoryInfo(dir);
                dto.Add(new ServerObject
                {
                    Name = info.Name,
                    FullPath = info.FullName.Replace("\\", "/"),
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    Type = ServerObjectType.Directory
                });
            }

            var files = Directory.GetFiles(directory);
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                dto.Add(new ServerObject
                {
                    Name = info.Name,
                    FullPath = info.FullName.Replace("\\", "/"),
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    Size = info.Length,
                    DisplaySize = FormatHelper.FormatDisplaySize(info.Length),
                    Extension = info.Name.Split('.').ToList().Last().ToUpper(),
                    Type = ServerObjectType.File
                });
            }

            var currDirectory = new DirectoryInfo(directory);
            var current = new ServerObject
            {
                Name = currDirectory.Name,
                FullPath = currDirectory.FullName
            };
            var counts = new List<string>();
            if (subdirs.Length > 0)
                counts.Add(subdirs.Length + (subdirs.Length == 1 ? " folder" : " folders"));
            if (files.Length > 0)
                counts.Add(files.Length + (files.Length == 1 ? " file" : " files"));

            current.CountDisplay = string.Join(", ", counts);

            return Ok(new { directory = current, contents = dto });
        }
    }
}