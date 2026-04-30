using Microsoft.AspNetCore.Mvc;
using System;
using UniversalMiddleware.Domain;

namespace UniversalMiddleware.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiKeyAuth]
public class ConnectionsController : ControllerBase {
    [HttpPost]
    public IActionResult CreateConnection([FromBody] Connection connection) {
        return Ok(connection);
    }
}
