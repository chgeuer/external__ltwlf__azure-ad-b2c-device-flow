# Device Flow Authentication for Azure AD B2C

## Component relations

```mermaid
flowchart LR
  Browser --> DeviceProxy[Device Proxy]
  Device -- show user code -.- User
  User --- Browser
  Browser-->AAD[Azure AD B2C]
  DeviceProxy-->AAD
  Device[Settop Box] --> DeviceProxy[Device Proxy]
  DeviceProxy-->Redis[(Redis)]     
  Device==>Application

```

## Interactions

```mermaid
sequenceDiagram
    actor User
    participant Browser
    participant Settop Box
    participant Device Proxy
    participant Redis
    participant Azure AD B2C
    participant Application
    User --) Settop Box: Turn on
    Settop Box ->> Device Proxy: Fetch Device Code
    Device Proxy ->> Redis: Store user_code and device_code
    Device Proxy ->> Settop Box: Code Response
    Settop Box --) User: Display "user_code" and Message
    activate Settop Box 
    rect rgb(243,245,159)
        Note over Settop Box, Device Proxy: Now the settop box continuously polls for status
        loop Every 5 seconds
            Settop Box ->> Device Proxy: Poll with device_code
            Device Proxy ->> Redis: Check for token response
            Device Proxy -->> Settop Box: Pending
        end
    end
    rect rgb(248,203,173)
        Note over User, Device Proxy: The user uses another device (browser, mobile) to perform the device login
        User --> Browser: Open device logon page
        Browser ->> Device Proxy: Supply user code
        Device Proxy ->> Browser: Redirect to AAD
        Browser ->> Azure AD B2C: Logon
        Azure AD B2C ->> Browser: Redirect with back to Device Proxy
        Browser ->> Device Proxy: Callback
        Device Proxy ->> Azure AD B2C: Fetch access_token for device
        Azure AD B2C ->> Device Proxy: Token response
        Device Proxy ->> Redis: Update entry under device_code with token
        Device Proxy --x Browser: Done
        Browser --x User: Done
    end
    rect rgb(243,245,159)
        Settop Box ->> Device Proxy: Poll with device_code
        Device Proxy ->> Redis: Fetch access_token and refresh token
        Device Proxy ->> Redis: Delete entry
        Device Proxy ->> Settop Box: Return access_token and refresh_token
        Settop Box ->> Settop Box: Store access_token and refresh_token
        deactivate Settop Box
    end
    rect rgb(191,223,255)
        Note right of Settop Box: Regular use of the access_token
        Settop Box ->> Application: Use app with access_token
    end
    rect rgb(197,224,180)
	    Note right of Settop Box: Refresh the tokens
        Settop Box ->> Device Proxy: refresh_token
        Device Proxy ->> Azure AD B2C: refresh (using the device proxy's client_secret)
        Azure AD B2C ->> Device Proxy: refreshed access_token and refresh_token
        Device Proxy ->> Settop Box: refreshed access_token and refresh_token
	end
```



## Example local config

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet",
    "Config:AppId": "",
    "Config:AppSecret": "",
    "Config:Tenant": "myb2c",
    "Config:RedirectUri": "http://localhost:7071/authorization_callback",
    "Config:SignInPolicy": "B2C_1A_signup_signin",
    "Config:VerificationUri": "http://localhost:7071",
    "Config:Redis": "localhost",
    // optional: configure custom html pages (e.g. load from a blob storage)
    "Config:UserCodePage": "http://localhost:8080/usercode.html",
    "Config:SuccessPage": "http://localhost:8080/success.html",
    "Config:ErrorPage": "http://localhost:8080/error.html"
  }
}
```


## Start on device

```
POST https://service-device-auth-flow.azurewebsites.net/oauth/device_authorization
Accept: */*
Content-Type: application/x-www-form-urlencoded
Cache-Control: no-cache

clientId=clientId&scope=offline_access
```

Example Response:

```
{
    "device_code":"9ab010de-9fe7-4f62-96c9-e9498004211e",
    "user_code":"211313","verification_uri":"https://service-device-auth-flow.azurewebsites.net/",
    "expires_in":300
}

```

## Navigate to website

https://service-device-auth-flow.azurewebsites.net/

Enter User Code and login

### Poll for token on device

```
POST https://service-device-auth-flow.azurewebsites.net/oauth/token
Accept: */*
Content-Type: application/x-www-form-urlencoded
Cache-Control: no-cache

grant_type=urn:ietf:params:oauth:grant-type:device_code&client_Id=mydeviceId&device_code=9ab010de-9fe7-4f62-96c9-e9498004211e
```

Pending example response:

```
HTTP/1.1 400 Bad Request
Content-Length: 33
Content-Type: application/json; charset=utf-8
Set-Cookie: ARRAffinity=dd716a6def04e48f4e433f7740cecb7f8a4f1c77d318c5480b769fc5157ad936;Path=/;HttpOnly;Domain=service-device-auth-flow.azurewebsites.net
Date: Wed, 30 Sep 2020 12:37:11 GMT

{
  "value": "authorization_pending"
}
```

Expired token

```
HTTP/1.1 400 Bad Request
Content-Length: 13
Content-Type: text/plain; charset=utf-8
Set-Cookie: ARRAffinity=dd716a6def04e48f4e433f7740cecb7f8a4f1c77d318c5480b769fc5157ad936;Path=/;HttpOnly;Domain=service-device-auth-flow.azurewebsites.net
Date: Wed, 30 Sep 2020 12:44:33 GMT

expired_token
```

Token

```
HTTP/1.1 200 OK
Content-Type: text/plain; charset=utf-8
Vary: Accept-Encoding
Set-Cookie: ARRAffinity=dd716a6def04e48f4e433f7740cecb7f8a4f1c77d318c5480b769fc5157ad936;Path=/;HttpOnly;Domain=service-device-auth-flow.azurewebsites.net
Date: Wed, 30 Sep 2020 12:46:31 GMT

{
    "access_Token":"eyJraWQiOiJ2VE96SmhwS3dIeD....",
    "token_type":null,
    "expires_in":0,
    "refresh_token":null,
    "scope":nul
}
```

## Example custom user code page

```html

<html>
<body>
<div>
    <form id="form" method="post">
        <label for="user_code">User Code</label>
        <input name="user_code" autofocus="autofocus"/>
        <button type="submit">Send</button>
    </form>
</div>
</body>
</html>
```

Instead of submitting the form directly you can also use JavaScript to submit.

```javascript
const form = document.getElementById("form");
window
    .fetch("", {
        method: "post",
        "Content-Type": "application/x-www-form-urlencoded",
        body: new FormData(form),
        headers: {
            "x-use-ajax": true,
        },
    })
    .then((resp) => {
        if (resp.ok) {
            resp
                .text()
                .then((redirectUrl) => (window.location.href = redirectUrl));
        } else {
            alert("error")
        }
    })
````

The success and error pages are just static HTML pages.