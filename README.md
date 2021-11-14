# CDorst.HttpApiGenerator

Create a .NET 6 ASP.NET Core minimal API application with health checks, OpenAPI spec, SwaggerUI, JWT bearer authentication, period batch logging to Splunk, and in-memory & Redis caching for calling SQL Server stored procedures with zero code and zero `.cs` files

![ezgif com-gif-maker-2](https://user-images.githubusercontent.com/18475870/141682084-2c6e4522-a2fb-4c0f-9f1b-e8766e6f32e4.gif)


`dotnet build` or `dotnet run` the sample `SampleApp.csproj` file (or use an IDE to debug, etc.) to build and run the sample application described below

The compiler generates source code from this README document using the `CDorst.HttpApiGenerator` project

Run the `SampleApp.csproj` application and navigate to `/index.html` to view the SwaggerUI

All of the source code for the application is inferred from the contents of this README file at compile time using C# .NET Source Generators

![image](https://user-images.githubusercontent.com/18475870/141681145-2faf2cd8-3142-47ae-9218-6f6263968705.png)


## Application

### Auth

JWT Authority `https://dev-yzmn8nbx.us.auth0.com/`

JWT Audience `https://localhost:5001/`

CORS AllowedHosts `*`

### Cache

Use memory

Use redis `https://localhost:6379/`

### Database

SQL Server `Server=.;Database=WideWorldImporters;Trusted_Connection=True;`

Download the database `WideWorldImporters-Full.bak` file [here at Microsoft's samples repo](http://go.microsoft.com/fwlink/?LinkID=800630) and "Right-click 'Databases' & 'Restore Database...'" in SQL Server Management Studio

### Contracts

```sql
PROCEDURE [Website].[ActivateWebsiteLogon]
@PersonID int required positive
@LogonName nvarchar(50) required
@InitialPassword nvarchar(40) required
CATCH
400 'Invalid PersonID'
```

```sql
PROCEDURE [Website].[ChangePassword]
@PersonID int required positive
@OldPassword nvarchar(40) required
@NewPassword nvarchar(40) required
CATCH
400 'Invalid Password Change'
```

```sql
PROCEDURE [Website].[InsertCustomerOrders]
@OrderList Website.OrderList {
    OrderReference int
    CustomerID int
    ContactPersonID int
    ExpectedDeliveryDate date
    CustomerPurchaseOrderNumber nvarchar(20)
    IsUndersuppliedBackordered bit
    Comments nvarchar(max)
    DeliveryInstructions nvarchar(max)
} required
@OrderLineList Website.OrderLineList {
    OrderReference int
    StockItemID int
    Description nvarchar(100)
    Quantity int
} required
@OrdersCreatedByPersonID int required positive
@SalespersonPersonID int positive
```

```sql
PROCEDURE [Website].[InvoiceCustomerOrders]
@OrdersToInvoice Website.OrderIDList {
    OrderID int required positive
} required
@PackedByPersonID int positive
@InvoicedByPersonID int required positive

CATCH
400 'At least one orderID either does not exist, is not priced, or is already invoiced'
```

```sql
PROCEDURE [Website].[RecordColdRoomTemperatures]
@SensorReadings Website.SensorDataList {
    SensorDataListID int
    ColdRoomSensorNumber int required positive
    RecordedWhen datetime required
    Temperature decimal required
} required

CATCH
500 'Unable to apply the sensor data'
```

```sql
PROCEDURE [Website].[RecordVehicleTemperature]
@FullSensorDataArray nvarchar(1000) required

CATCH
400 'FullSensorDataArray must be valid JSON data'
400 'Valid JSON was supplied but does not match the temperature recordings array structure'
```

```sql
PROCEDURE [Website].[SearchForCustomers]
@SearchText nvarchar(1000) required
@MaximumRowsToReturn int default(10) positive

RETURNS
CustomerID int
CustomerName nvarchar
CityName nvarchar
PhoneNumber nvarchar
FaxNumber nvarchar
PrimaryContactFullName nvarchar
PrimaryContactPreferredName nvarchar

CACHE 15 MINUTES
HTTP GET
```

```sql
PROCEDURE [Website].[SearchForPeople]
@SearchText nvarchar(1000) required
@MaximumRowsToReturn int default(10)

RETURNS
PersonID int
FullName nvarchar
PreferredName nvarchar
Relationship nvarchar
Company nvarchar

CACHE 15 MINUTES
HTTP GET
```

```sql
PROCEDURE [Website].[SearchForStockItems]
@SearchText nvarchar(1000) required
@MaximumRowsToReturn int default(10)

RETURNS
StockItemID int
StockItemName nvarchar

CACHE 5 MINUTES
HTTP GET
```

```sql
PROCEDURE [Website].[SearchForStockItemsByTags]
@SearchText nvarchar(1000) required
@MaximumRowsToReturn int default(10)

RETURNS
StockItemID int
StockItemName nvarchar

CACHE 5 MINUTES
HTTP GET
```

```sql
PROCEDURE [Website].[SearchForSuppliers]
@SearchText nvarchar(1000) required
@MaximumRowsToReturn int default(10)

RETURNS
SupplierID int
SupplierName nvarchar
CityName nvarchar
PhoneNumber nvarchar
FaxNumber nvarchar
PrimaryContactFullName nvarchar
PrimaryContactPreferredName nvarchar

CACHE 4 HOURS
HTTP GET
```
