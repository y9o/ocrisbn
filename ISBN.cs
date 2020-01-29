namespace ocrisbn
{
    class ISBN
    {
        public string ISBN10;
        public string ISBN13;
        public int Rank;
        public ISBN(string no)
        {
            if (no.Length == 13)
            {
                ISBN13 = no;
                int sum = 0;
                for (int i = 3; i < 12; i++)
                {
                    sum += (no[i] - '0') * (13 - i);
                }
                sum = 11 - (sum % 11);
                ISBN10 = no.Substring(3, 9);
                if (sum == 10)
                {
                    ISBN10 += "X";
                }
                else
                {
                    if (sum == 11)
                        sum = 0;
                    ISBN10 += sum.ToString();
                }
            }
            else if (no.Length == 10)
            {
                ISBN10 = no;
                int sum = 0;
                ISBN13 = "978" + no.Substring(0, 9);
                for (int i = 0; i < 12; i++)
                {
                    sum += (ISBN13[i] - '0') * (i % 2 == 0 ? 1 : 3);
                }
                sum = sum % 10;
                if (sum > 0)
                    ISBN13 += (10 - sum).ToString();
                else
                    ISBN13 += "0";
            }
            else
            {
                Rank = -1;
            }

        }
    }
}
