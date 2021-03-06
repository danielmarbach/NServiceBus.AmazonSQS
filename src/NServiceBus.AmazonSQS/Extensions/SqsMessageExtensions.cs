﻿namespace NServiceBus.AmazonSQS
{
    using System;
    using System.IO;
    using Amazon.SQS.Model;
    using Amazon.S3;
    using Transport;
    using System.Threading.Tasks;
    using System.Threading;
    using Amazon;

    internal static class SqsMessageExtensions
    {
        public static async Task<IncomingMessage> ToIncomingMessage(this SqsTransportMessage sqsTransportMessage,
            IAmazonS3 amazonS3,
            SqsConnectionConfiguration connectionConfiguration,
            CancellationToken cancellationToken)
        {
            var messageId = sqsTransportMessage.Headers[Headers.MessageId];

            byte[] body;

            if (!string.IsNullOrEmpty(sqsTransportMessage.S3BodyKey))
            {
                var s3GetResponse = await amazonS3.GetObjectAsync(connectionConfiguration.S3BucketForLargeMessages, 
                    sqsTransportMessage.S3BodyKey,
                    cancellationToken).ConfigureAwait(false);

                body = new byte[s3GetResponse.ResponseStream.Length];
                using (BufferedStream bufferedStream = new BufferedStream(s3GetResponse.ResponseStream))
                {
                    int count;
                    int transferred = 0;
                    const int maxChunkSize = 8 * 1024;
                    int bytesToRead = Math.Min(maxChunkSize, body.Length - transferred);
                    while ((count = await bufferedStream.ReadAsync(body, transferred, bytesToRead, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        transferred += count;
                        bytesToRead = Math.Min(maxChunkSize, body.Length - transferred);
                    }
                }
            }
            else
            {
                body = Convert.FromBase64String(sqsTransportMessage.Body);
            }

            return new IncomingMessage(messageId, sqsTransportMessage.Headers, body);
        }

        public static DateTime GetSentDateTime(this Message message)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var result = epoch.AddMilliseconds(long.Parse(message.Attributes["SentTimestamp"]));
            // Adjust for clock skew between this endpoint and aws.
            // https://aws.amazon.com/blogs/developer/clock-skew-correction/
            return result + AWSConfigs.ClockOffset;
        }
    }
}
