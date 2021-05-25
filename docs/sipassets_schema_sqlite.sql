create table SIPDomains
(
 ID nvarchar(36) not null,
 Domain nvarchar(128) not null,			-- The domain name.
 AliasList nvarchar(1024),				-- If not null indicates a semi-colon delimited list of aliases for the domain.
 Inserted nvarchar not null,			-- datetime
 Primary Key(ID),
 Unique(domain)
);

create table SIPDialPlans
(
 ID nvarchar(36) not null,
 DialPlanName varchar(64) not null,			-- Name the owner has assigned to the dialplan to allow them to choose between their different ones.
 DialPlanScript varchar,
 Inserted nvarchar not null,
 LastUpdate nvarchar not null,
 AcceptNonInvite int(2) not null default 0,	-- If true the dialplan will accept non-INVITE requests.
 Primary Key(ID),
 Unique(DialPlanName)
);

create table SIPAccounts
(
 ID nvarchar(36) not null,
 DomainID nvarchar(36) not null,
 SIPDialPlanID nvarchar(36) null,
 SIPUsername nvarchar(32) not null,
 HA1Digest nvarchar(32) not null,
 IsDisabled int(2) not null default 0,
 Inserted nvarchar not null,
 Primary Key(ID),
 Foreign Key(DomainID) references SIPDomains(ID) on delete cascade on update cascade,
 Foreign Key(SIPDialPlanID) references SIPDialPlans(ID) on delete cascade on update cascade,
 Unique(SIPUsername, DomainID)
);

create table SIPRegistrarBindings
(
 ID nvarchar(36) not null,				-- A unique id assigned to the binding in the Registrar.
 SIPAccountID nvarchar(36) not null,
 UserAgent nvarchar(1024),
 ContactURI nvarchar(767) not null,			-- This is the URI the user agent sent in its Contact header requesting a binding for.
 Expiry int not null,
 RemoteSIPSocket nvarchar(64) not null,
 ProxySIPSocket nvarchar(64),
 RegistrarSIPSocket nvarchar(64) null,
 LastUpdate nvarchar not null,
 ExpiryTime nvarchar not null,
 Primary Key(ID),
 Foreign Key(SIPAccountID) references SIPAccounts(ID)
);

create table CDR
(
 ID nvarchar(36) not null,
 Inserted nvarchar not null,
 Direction varchar(3) not null,					-- In or Out with respect to the proxy.
 Created nvarchar not null,				-- Time the cdr was created by the proxy.
 DstUser varchar(128),							-- The user portion of the destination URI.
 DstHost varchar(128) not null,					-- The host portion of the destination URI.
 DstUri varchar(1024) not null,					-- The full destination URI.
 FromUser varchar(128),							-- The user portion of the From header URI.
 FromName varchar(128),							-- The name portion of the From header.
 FromHeader varchar(1024),						-- The full From header.
 CallID varchar(256) not null,					-- The Call-ID of the call.
 LocalSocket varchar(64) null,				    -- The socket on the proxy used for the call.
 RemoteSocket varchar(64) null,				    -- The remote socket used for the call.
 BridgeID nvarchar(36) null,   			    -- If the call was involved in a bridge the id of it.
 InProgressAt nvarchar null default null, -- The time of the last info response for the call.
 InProgressStatus int,							-- The SIP response status code of the last info response for the call.
 InProgressReason varchar(512),					-- The SIP response reason phrase of the last info response for the call.
 RingDuration int,								-- Number of seconds the call was ringing for.
 AnsweredAt nvarchar null default null,	-- The time the call was answered with a final response.
 AnsweredStatus int,							-- The SIP response status code of the final response for the call.
 AnsweredReason varchar(512),					-- The SIP response reason phrase of the final response for the call.
 Duration int,									-- Number of seconds the call was established for.
 HungupAt nvarchar null default null,	    -- The time the call was hungup.
 HungupReason varchar(512),						-- The SIP response Reason header on the BYE request if present.
 Primary Key(ID)
);

create table SIPCalls
(
 ID nvarchar(36) not null,
 CDRID nvarchar(36) null,
 LocalTag varchar(64) not null,
 RemoteTag varchar(64) not null,
 CallID varchar(128) not null,
 CSeq int not null,
 BridgeID nvarchar(36) not null,
 RemoteTarget varchar(256) not null,
 LocalUserField varchar(512) not null,
 RemoteUserField varchar(512) not null,
 ProxySendFrom varchar(64),
 RouteSet varchar(512),
 CallDurationLimit int,
 Direction varchar(3) not null,					-- In or Out with respect to the proxy.
 Inserted nvarchar not null,
 RemoteSocket varchar(64) not null,	
 Primary Key(ID),
 Foreign Key(CDRID) references CDR(ID) on delete cascade on update cascade
);

CREATE TABLE [SessionCache](
	[Id] [nvarchar](449) NOT NULL,
	[Value] [varbinary] NOT NULL,
	[ExpiresAtTime] [nvarcharoffset](7) NOT NULL,
	[SlidingExpirationInSeconds] [bigint] NULL,
	[AbsoluteExpiration] [nvarcharoffset](7) NULL,
	PRIMARY KEY (Id)
);

create table WebRTCSignals
(
 ID nvarchar(36) not null,
 "From" varchar(256) not null,
 "To" varchar(256) not null,
 SignalType varchar(16) not null,
 Signal varchar not null,
 Inserted nvarchar not null,
 DeliveredAt nvarchar null,
 Primary Key(ID)
);

INSERT into SIPDomains (ID, Domain, AliasList, Inserted) VALUES ('899e48ef-1267-4b53-8a1c-476a176a4e80', '192.168.0.50', '[::1];127.0.0.1;localhost', '2021-01-05T10:13:00.000+00:00');
insert into SIPDialPlans values ('AF79FF67-48B4-4FAA-81B5-075EA5F7FEB7', 'default', 'var inUri = uasTx.TransactionRequestURI;   
var body = uasTx.TransactionRequest.Body;    
return inUri.User switch  
{      
    "100" => new fwd("100@192.168.0.48", body),               // Hello World from Asterisk.      
    "101" => new fwd("100@[2a02:8084:6981:7880::1ff]", body), // Hello World from Asterisk with IPv6 RTP socket.     
    "123" => new fwd("time@sipsorcery.com", body),      
    "215" => new fwd("music@iptel.org", body),            
    "1000" => new fwd("1000@192.168.0.43:5080", body),  // FreeSWITCH Clock.      
    "9172" => new fwd("9172@192.168.0.43:5080", body),  // FreeSWITCH Clock.      
    "9173" => new fwd("9173@192.168.0.43:5080", body),  // FreeSWITCH Hello World.      
    "9192" => new fwd("9192@192.168.0.43:5080", body),  // FreeSWITCH Show Info.      
    "9200" => new fwd("9200@192.168.0.43:5080", body),  // FreeSWITCH Echo.            
    "helloworld" => new fwd("100@192.168.0.48", body),      
    "videotest" => new fwd("100@192.168.0.48", body),            
    _ => null  
};
', '2021-01-09 20:37:27.993+00:00', '2021-02-08 22:02:26.643+00:00', 0);
insert into SIPAccounts values ('95AAB7EA-9BD1-4256-B57E-8E9EBACC8960', '899e48ef-1267-4b53-8a1c-476a176a4e80', null, 'user', 'bba0da00f6b94f726912a3ab6342da6e', 0, '2021-02-01 13:13:09.050+00:00');
insert into SIPAccounts values ('22369755-9D57-4743-A5C6-F83F1CDEA20E', '899e48ef-1267-4b53-8a1c-476a176a4e80', null, '197660', 'daa37feeefe043420774c9bd9bae725f', 0, '2021-02-01 13:13:09.050+00:00');