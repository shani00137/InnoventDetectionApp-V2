using System;
using System.Collections.Generic;
using System.Text;

namespace Model
{
    public class ResultRequestModel
    {
        public  List<ResutlModel>? ResutlModelList { get; set; }
        public List<string>? CapturedImagesUrl { get; set; }
        public DateTime? CreatedTime { get; set; }
    }
}
