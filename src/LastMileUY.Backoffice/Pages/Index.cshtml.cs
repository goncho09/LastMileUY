using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages;

[Authorize]
public class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}