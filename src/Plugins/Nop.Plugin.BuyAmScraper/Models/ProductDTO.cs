using System;
using System.Net;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.BuyAmScraper.Models
{
    public record ProductDTO
    {
        private string _imageUrl;
        private byte[] _image;

        private byte[] DownloadImage()
        {
            using var httpClient = new WebClient();
            _image = httpClient.DownloadData(_imageUrl);
            return _image;
        }
        
        public string Sku { get; set; }
        public string Name { get; set; }
        public int Price { get; set; }
        public string ShortDescription { get; set; }
        public string FullDescription { get; set; }
        public byte[] Image { get => _image ?? DownloadImage(); internal set => _image = value; }
        public string ImageFileName { get; set; }
        public string Category { get; set; }
        public string SubCategory { get; set; }
        public string Partner { get; set; }

        public ProductDTO()
        {
            
        }
        public ProductDTO(string imageUrl)
        {
            _imageUrl = imageUrl;
        }
    }
}