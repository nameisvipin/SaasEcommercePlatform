using SaasEcommerce.Core.Entities;

namespace SaasEcommerce.Core.Common;

public static class CatalogQualityCalculator
{
    public static int CalculateScore(Product product)
    {
        int score = 0;

        // 1. Title length between 20 and 150 chars (+20 pts)
        if (!string.IsNullOrWhiteSpace(product.Title) && product.Title.Length >= 20)
            score += 20;
        else if (!string.IsNullOrWhiteSpace(product.Title))
            score += 10;

        // 2. Rich Description > 100 chars (+15 pts)
        if (!string.IsNullOrWhiteSpace(product.RichDescriptionHtml) && product.RichDescriptionHtml.Length >= 100)
            score += 15;

        // 3. At least 4 Key Feature Bullet Points (+20 pts)
        if (product.BulletPoints is { Count: >= 4 })
            score += 20;
        else if (product.BulletPoints is { Count: >= 1 })
            score += 10;

        // 4. Image Count >= 3 WebP images (+25 pts)
        int imageCount = product.MediaGallery.Count(m => m.MediaType == MediaType.ImageWebP);
        if (imageCount >= 3)
            score += 25;
        else if (imageCount >= 1)
            score += 10;

        // 5. Product Demo Video Present (+10 pts)
        bool hasVideo = product.MediaGallery.Any(m => m.MediaType == MediaType.VideoMp4);
        if (hasVideo)
            score += 10;

        // 6. Valid HSN Code & SEO Keywords (+10 pts)
        if (!string.IsNullOrWhiteSpace(product.HsnCode) && product.SeoKeywords is { Count: >= 3 })
            score += 10;

        return Math.Min(100, score);
    }
}