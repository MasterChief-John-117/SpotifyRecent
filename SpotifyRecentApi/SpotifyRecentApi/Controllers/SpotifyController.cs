﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace SpotifyRecentApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SpotifyController : ControllerBase
    {
        private readonly ILogger<SpotifyController> _logger;

        public SpotifyController(ILogger<SpotifyController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Get()
        {
            Program.EnsureTokenUpToDate();

            var cache = Program.GetCache();
            if (cache == null || cache.Tracks == null || cache.Tracks.Count == 0)
            {
                var config = new Config().Load();

                WebClient client = new WebClient();
                client.Headers.Clear();
                client.Headers[HttpRequestHeader.Authorization] = $"Bearer {config.AccessToken}";

                var recentstr = client.DownloadString("https://api.spotify.com/v1/me/player/recently-played");
                var recentType = new
                {
                    items = new[] {
                    new
                    {
                        track = new
                        {
                            artists = new[]
                            {
                                new
                                {
                                    name = "string"
                                }
                            },
                            name = "string"
                            }
                        }
                    }
                };

                var recent = JsonConvert.DeserializeAnonymousType(recentstr, recentType);

                History history = new History();

                foreach (var track in recent.items)
                {
                    history.Tracks.Add(new Track { Name = track.track.name, Artists = track.track.artists.Select(a => a.name).ToList() });
                }

                var currstr = client.DownloadString("https://api.spotify.com/v1/me/player/currently-playing");
                var currType = new
                {
                    is_playing = false,
                    item = new
                    {
                        artists = new[]
                            {
                                new
                                {
                                    name = "string"
                                }
                            },
                        name = "string"
                    }
                };
                var curr = JsonConvert.DeserializeAnonymousType(currstr, currType);
                if (curr != null && curr.item != null)
                {
                    history.CurrentTrack = new Track { Name = curr.item.name, Artists = curr.item.artists.Select(a => a.name).ToList() };
                }
                else
                    history.CurrentTrack = null;

                Program.SetCache(history);
                return new JsonResult(history);
            }
            else
                return new JsonResult(cache);
        }

        [HttpGet("login")]
        public IActionResult GetLogin()
        {
            HttpContext.Response.Headers.Add("Location", $"https://accounts.spotify.com/en/authorize?client_id=11488a428ed449e2a6865f6ae51b64c9&response_type=code&redirect_uri=http:%2F%2F{HttpContext.Request.Host}%2Fapi%2Fspotify%2Fcallback&scope=user-read-recently-played%20user-read-currently-playing%20user-read-email");
            return new StatusCodeResult(307);
        }

        [HttpGet("callback")]
        public IActionResult FinishAuth([FromQuery(Name = "code")] string code)
        {
            var config = new Config().Load();
            WebClient client = new WebClient();
            client.Headers.Clear();
            client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
            var reqparm = new System.Collections.Specialized.NameValueCollection();
            reqparm.Add("grant_type", "authorization_code");
            reqparm.Add("code", code);
            reqparm.Add("redirect_uri", $"http://{Request.Host}/api/spotify/callback");
            reqparm.Add("client_id", config.ClientId);
            reqparm.Add("client_secret", config.ClientSecret);

            try
            {
                byte[] responsebytes = client.UploadValues("https://accounts.spotify.com/api/token", "POST", reqparm);
                string responsebody = Encoding.UTF8.GetString(responsebytes);
                var responseType = new
                {
                    access_token = "",
                    refresh_token = "",
                };

                var response = JsonConvert.DeserializeAnonymousType(responsebody, responseType);

                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.Authorization] = $"Bearer {response.access_token}";
                    string meresp = wc.DownloadString("https://api.spotify.com/v1/me");
                    var meType = new
                    {
                        email = "",
                    };
                    var me = JsonConvert.DeserializeAnonymousType(meresp, meType);

                    if(me.email.Equals(config.ValidEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        config.AccessToken = response.access_token;
                        config.RefreshToken = response.refresh_token;
                        config.ExpiresEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
                        config.Save();
                        return new OkObjectResult($"Logged in as {me.email}");
                    }
                    else
                    {
                        return new BadRequestObjectResult($"Email {me.email} not allowed");
                    }
                }
            }
            catch(Exception ex)
            {
                return new BadRequestObjectResult("Bad Request. Please try logging in again");
            }
        }
    }
}
