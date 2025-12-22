using System;
using System.Collections.Generic;
using System.Text;


namespace SQLite
{

    using System.Text.RegularExpressions;
    public class ORCWithReg()
    {
        public static OCRModel ApplyRulesOnOCR(string ocrText, List<RuleModel> rules)
        {
            var result = new OCRModel();

            foreach (var rule in rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Rule))
                    continue;

                try
                {
                    var regex = new Regex(rule.Rule, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    var matches = regex.Matches(ocrText);

                    foreach (Match match in matches)
                    {
                        if (!match.Success) continue;

                        string value = match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : match.Value.Trim();

                        // Map to OCRModel based on rule name
                        if (rule.RuleName.Equals("Supplier", StringComparison.OrdinalIgnoreCase))
                        {
                            result.SupplierName = value;
                        }
                        else if (rule.RuleName.Equals("ExpiryDate", StringComparison.OrdinalIgnoreCase))
                        {
                            result.ExpiryDate = value;
                        }
                    }
                }
                catch
                {
                    // Ignore invalid regex for now
                    continue;
                }
            }

            return result;
        }

    }



}
