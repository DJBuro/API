using System;
using System.Collections.Generic;
using System.Linq;
using System.Monads;
using MyAndromeda.Data.DataWarehouse.Models;
using MyAndromeda.Framework.Dates;
using MyAndromeda.Services.Ibs.IbsWebOrderApi;

namespace MyAndromeda.Services.Ibs.Models
{
    public static class OrderExtensions
    {
        public static AddOrderRequest TransformDraft(this OrderHeader orderHeader, IDateServices dateServices)
        {
            var placedTime = dateServices.ConvertToLocalFromUtc(orderHeader.OrderPlacedTime).GetValueOrDefault();
            var wantedTime = dateServices.ConvertToLocalFromUtc(orderHeader.OrderWantedTime).GetValueOrDefault();

            if (wantedTime < DateTime.Now)
            {
                var future = DateTime.Now.AddMinutes(5);

                wantedTime = future;

                //model.WantedOrderDay = future.Day;
                //model.WantedOrderMonth = future.Month;
                //model.WantedOrderYear = future.Year;
            }


            var timeSlot = string.Format("{0:00}{1:00}", wantedTime.Hour, wantedTime.Minute);

            var model = new AddOrderRequest()
            {
                //Commit = true,
                OrderType = eOrderType.eDelivery,
                //TableNumber = 1,
                //eOrderType.eDelivery,
                //orderHeader.OrderType.Equals("DELIVERY", StringComparison.InvariantCultureIgnoreCase)
                //    ? eOrderType.eDelivery
                //    : eOrderType.eCollection,
                //ConfirmOnPos = false,
                CustomerNo = 0,
                CustomerDetails =
                    orderHeader.TransformCustomerAddress(),
                DeliveryInstructions =
                    orderHeader.Customer.Address == null
                    ? "Unknown"
                    : orderHeader.Customer.Address.Directions,
                UserReference = orderHeader.ExternalOrderRef,

                //Items = orderHeader.OrderLines.Select(e=> e.Transform()).ToArray(),
                OrderPlacedDay = placedTime.Day,
                OrderPlacedMonth = placedTime.Month,
                OrderPlacedYear = placedTime.Year,
                OrderPlacedHour = placedTime.Hour,
                OrderPlacedMin = placedTime.Minute,
                TimeSlotFrom = Convert.ToInt32(timeSlot),
                TimeSlotTo = Convert.ToInt32(timeSlot),
                PayOnCollectionOrDelivery = orderHeader.paytype.Equals("PAYLATER"),
                WantedOrderDay = wantedTime.Day,
                WantedOrderMonth = wantedTime.Month,
                WantedOrderYear = wantedTime.Year,
            };

            var items = new List<cWebTransItem>();
            int counter = 1;

            

            

            bool isCash = orderHeader.paytype.ToUpper().Equals("PAYLATER") || orderHeader.paytype.ToUpper().Equals("CASH");

            bool hasDeliveryCharge = orderHeader.DeliveryCharge > 0;

            if (hasDeliveryCharge)
            {
                items.Add(new cWebTransItem()
                {
                    m_dGrossValue = orderHeader.DeliveryCharge,
                    m_eLineType = eWebOrderLineType.ePLU,
                    m_iLineNum = counter,
                    m_iQty = 1,
                    m_szDescription = "Delivery Charge",
                    m_lOffset = 15073
                });
                counter++;
            }

            

            //too detailed. 
            

            //old payment
            //items.Add(new cWebTransItem()
            //{
            //    m_dGrossValue = orderHeader.FinalPrice,
            //    m_eLineType = eWebOrderLineType.ePayment,
            //    m_dStockQty = 1,
            //    m_iLineNum = counter,
            //    m_lOffset = isCash ? 1 : 2,
            //    m_szDescription = isCash ? "Cash" : "Card"
            //});

            model.Items = items;
            
            return model;
        }

        public static int[] CheckForMatchingFoodIds(this IEnumerable<OrderLine> orderItems, IbsRamesesTranslation[] translationItems) 
        {
            var someFoodItemsDontMatch = orderItems
                                          .Where(e => !translationItems.Any(k => k.RamesesMenuItemId == e.ProductID))
                                          .Select(e => e.ProductID.GetValueOrDefault())
                                          .ToArray();

            return someFoodItemsDontMatch;
        }

        public static AddOrderRequest AddFoodItems(this AddOrderRequest orderRequest, OrderHeader orderHeader, IbsRamesesTranslation[] translationItems) 
        {
            var counter = orderRequest.Items.Count + 1;

            var matchedItems = orderHeader.OrderLines.Select(e=> new {  
                Item = e,
                Plu = translationItems.Where(t => t.RamesesMenuItemId == e.ProductID)
                                    .Select(t => t.PluNumber)
                                    .FirstOrDefault()
            });

            var groupedItems = matchedItems.GroupBy(e => e.Plu);

            foreach (var group in groupedItems)
            {
                var translation = group.First();
                var item = translation.Item;
                var pluNumber = translation.Plu;

                var ibsItem = item.TransformOrderLines(counter);

                if (pluNumber == null) { throw new NullReferenceException("plu number missing");  }

                ibsItem.m_lOffset = pluNumber;
                ibsItem.m_dStockQty = group.Count();


                int groupPrice = group.Sum(e => e.Item.Price.GetValueOrDefault());
                decimal a = groupPrice;
                
                ibsItem.m_dGrossValue = a / 100;

                orderRequest.Items.Add(ibsItem);
                
                counter++;
            }

            return
                orderRequest;
        }

        public static AddOrderRequest AddPaymentLines(this AddOrderRequest orderRequest, OrderHeader orderHeader, List<IbsPaymentTypeTranslation> ibsPaymentTranslation) 
        {
            var counter = orderRequest.Items.Count + 1;

            foreach (var payment in orderHeader.OrderPayments)
            {
                orderRequest.Items.Add(payment.TransformPaymentLine(ibsPaymentTranslation, counter));
                counter++;
            }

            return orderRequest;
        }

        public static AddOrderRequest AddDiscounts(this AddOrderRequest orderRequest, OrderHeader orderHeader)
        {
            var counter = orderRequest.Items.Count + 1;

            foreach (var item in orderHeader.OrderDiscounts)
            {
                orderRequest.Items.Add(item.TransformDiscounts(counter));
                counter++;
            }

            return orderRequest;
        }

        public static AddOrderRequest AddTip(this AddOrderRequest orderRequest, OrderHeader orderHeader) 
        {
            var counter = orderRequest.Items.Count + 1;

            decimal tipValue = orderHeader.Tips.GetValueOrDefault();
            bool hasTip = tipValue > 0;

            if (hasTip)
            {
                decimal tipValue2 = tipValue / 100;
                orderRequest.Items.Add(new cWebTransItem()
                {
                    m_dGrossValue = tipValue2,
                    m_eLineType = eWebOrderLineType.eTip,
                    m_iLineNum = counter,
                    m_iQty = 1,
                    m_szDescription = "Tip Added",
                    m_lOffset = 1
                });
            }

            return orderRequest;
        }

        public static cWebTransItem TransformPaymentLine(this OrderPayment payment, List<IbsPaymentTypeTranslation> paymentTypeMappings, int lineCount) 
        {
            decimal value = payment.Value;

            string paymentType = string.IsNullOrWhiteSpace(payment.PayTypeName) ? payment.PaymentType : payment.PayTypeName;
            //paymenttype - cash / paylater 
            //paytypename - visa / internet / 

            int offset = 1; //cash 

            bool isCash = paymentType.Equals("PAYLATER") || paymentType.Equals("CASH");

            if (!isCash)
            {
                var mapping = paymentTypeMappings.FirstOrDefault(e => e.OrderPaymentTypeName.Equals(paymentType, StringComparison.CurrentCultureIgnoreCase));

                if (mapping == null)
                {
                    //who knows 
                    offset = 2; //card 
                }
                else 
                {
                    //cash = 1, card = 2, account = 3, amex = 10 etc ... 
                    offset = mapping.MediaNumber;
                    paymentType = mapping.MediaType; //switch to whatever they have written down.
                }
            }

            return new cWebTransItem()
            {
                m_dGrossValue = value / 100,
                m_eLineType = eWebOrderLineType.ePayment,
                m_dStockQty = 1,
                m_iLineNum = lineCount,
                m_lOffset = offset,
                m_szDescription = payment.PayTypeName
            };
        }

        public static cWebTransItem TransformDiscounts(this OrderDiscount orderDiscount, int lineCount)
        {
            decimal value = Convert.ToDecimal(orderDiscount.DiscountTypeAmount);

            return new cWebTransItem()
            {
                m_eLineType = eWebOrderLineType.eAdjustment,
                m_dGrossValue = value / 100,
                m_szDescription = orderDiscount.InitialDiscountReason,
                m_szCode = orderDiscount.InitialDiscountReason,
                m_lOffset = 1
            };
        }

        public static cWebTransItem TransformOrderLines(this OrderLine orderline, int lineCount)
        {
            var value = orderline.Price.GetValueOrDefault();
            var value2 = Convert.ToDecimal(value);

            return new cWebTransItem()
            {
                m_lOffset = orderline.ProductID.GetValueOrDefault(),
                //m_szCode 
                //m_szDescription 
                m_iQty = orderline.Qty.GetValueOrDefault(1),
                m_dGrossValue = value2 / 100,
                m_dStockQty = orderline.Qty.GetValueOrDefault(1),
                m_eLineType = eWebOrderLineType.ePLU,
                m_iLineNum = lineCount,
                m_szDescription = orderline.Description
            };
        }


        public static cOrderCustomerDetails TransformCustomerAddress(this OrderHeader orderHeader)
        {
            var customerAddress = orderHeader.CustomerAddress;

            if (customerAddress == null)
            {
                customerAddress = new CustomerAddress()
                {
                    RoadNum = "",
                    RoadName = "",
                    City = "",
                    State = "",
                    ZipCode = "SM6 0DZ"
                };
            }

            var contacts = customerAddress.Customer.Contacts;
            var email = contacts.FirstOrDefault(e => e.ContactTypeId == 0);
            var phone = contacts.FirstOrDefault(e => e.ContactTypeId == 1);

            return new cOrderCustomerDetails()
            {
                m_szAddress1 = customerAddress.RoadNum,
                m_szAddress2 = customerAddress.RoadName,
                m_szAddress3 = customerAddress.City,
                m_szAddress4 = customerAddress.State,
                m_szPostcode = customerAddress.ZipCode,
                m_szForename = orderHeader.Customer.FirstName,
                m_szSurname = orderHeader.Customer.LastName,
                m_szPhone =
                    phone == null ? "0123456789"
                    : phone.Value,
                m_szEMail =
                    email == null ? "unknown@andromeda.com"
                    : email.Value
            };
        }

        public static void ValidateModel(AddOrderRequest request)
        {
            request.WantedOrderDay.Check(e => e > 0, e => new ArgumentException("Wanted 'day' is not set"));
            request.WantedOrderMonth.Check(e => e > 0, e => new ArgumentException("Wanted 'month' is not set"));
            request.WantedOrderYear.Check(e => e > 0, e => new ArgumentException("Wanted 'year' is not set"));

            request.TimeSlotFrom.Check(e => e > 0, e => new ArgumentException("'TimeSlotFrom' not set"));
            request.TimeSlotTo.Check(e => e > 0, e => new ArgumentException("'TimeSlotTo' not set"));

            request.CustomerNo.Check(e => e > 0, e => new ArgumentException("'Customer id is not set'"));

            request.Items.CheckNull("Items");

            request.Items.Check(e => e.Any(r => r.m_eLineType == eWebOrderLineType.ePLU), e => new ArgumentException("'There are no food items in the order'"));
            request.Items.Check(e => e.Any(r => r.m_eLineType == eWebOrderLineType.ePayment), e => new ArgumentException("'There are no payment items in the order'"));

            request.OrderPlacedDay.Check(e => e > 0, e => new ArgumentException("Placed 'day' is not set"));
            request.OrderPlacedMonth.Check(e => e > 0, e => new ArgumentException("Placed 'month' is not set"));
            request.OrderPlacedYear.Check(e => e > 0, e => new ArgumentException("Placed 'year' is not set"));
            request.OrderPlacedHour.Check(e => e > 0, e => new ArgumentException("Placed 'Hour' is not set"));
            request.OrderPlacedMin.Check(e => e > 0, e => new ArgumentException("Placed 'Min' is not set"));
        }
    }
}