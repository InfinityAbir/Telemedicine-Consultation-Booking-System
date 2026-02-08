using Microsoft.AspNetCore.Mvc;

public class ChatController : Controller
{
    private readonly ChatService _chat;

    public ChatController(ChatService chat)
    {
        _chat = chat;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] ChatRequest req)
    {
        var reply = await _chat.GetReply(req.Message);
        return Json(new { answer = reply });
    }
}

public class ChatRequest
{
    public string Message { get; set; }
}
